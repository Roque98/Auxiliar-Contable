#!/usr/bin/env bash
# Preparacion UNA SOLA VEZ de un VPS Ubuntu 22.04/24.04 para IgasConsole.
# Ejecutar como root:  bash setup-servidor.sh
#
# Deja el servidor listo para que el workflow de deploy solo tenga que copiar
# artefactos. No despliega nada por si mismo.
set -euo pipefail

USUARIO_APP=zia
USUARIO_DEPLOY=${USUARIO_DEPLOY:-deploy}
RAIZ=/srv/zia-console

echo "==> Paquetes base"
apt-get update
apt-get install -y curl ca-certificates gnupg nginx postgresql rsync ufw

echo "==> Runtime de ASP.NET Core 10"
# El feed de Ubuntu no lleva .NET 10; se anade el de Microsoft.
if ! command -v dotnet >/dev/null 2>&1; then
    source /etc/os-release
    curl -fsSL "https://packages.microsoft.com/config/ubuntu/${VERSION_ID}/packages-microsoft-prod.deb" \
        -o /tmp/packages-microsoft-prod.deb
    dpkg -i /tmp/packages-microsoft-prod.deb
    rm /tmp/packages-microsoft-prod.deb
    apt-get update
fi
# aspnetcore-runtime, no dotnet-runtime: la API necesita el stack web.
apt-get install -y aspnetcore-runtime-10.0

echo "==> Usuario de servicio y directorios"
id -u "$USUARIO_APP" >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin "$USUARIO_APP"
# El usuario de despliegue tiene que existir ANTES del chown de abajo.
id -u "$USUARIO_DEPLOY" >/dev/null 2>&1 || adduser --disabled-password --gecos "" "$USUARIO_DEPLOY"
mkdir -p "$RAIZ"/{staging,current}
# Las versiones publicadas del programa del nodo de estación (deploy/nodo/publicar.sh). Las sirve la
# API a los nodos; la consola solo las lee (Nodos__CarpetaDePublicaciones en /etc/zia-console/env).
mkdir -p "$RAIZ/nodo"
# Las claves de DataProtection van FUERA de $RAIZ: cada deploy reemplaza current/
# y se las llevaria por delante -> se invalidan las cookies y el login da 400.
mkdir -p /var/lib/zia-console /var/log/zia-console/api /var/log/zia-console/migrator /etc/zia-console
chown -R "$USUARIO_APP:$USUARIO_APP" "$RAIZ/current" /var/lib/zia-console /var/log/zia-console
# El workflow entra como $USUARIO_DEPLOY y hace rsync a staging/ y a $RAIZ/deploy.sh:
# si esto fuera de $USUARIO_APP, la subida fallaria con "Permission denied".
chown "$USUARIO_DEPLOY:$USUARIO_DEPLOY" "$RAIZ"
chown -R "$USUARIO_DEPLOY:$USUARIO_DEPLOY" "$RAIZ/staging"

echo "==> Base de datos"
sudo -u postgres psql -v ON_ERROR_STOP=1 <<'SQL'
SELECT 'CREATE ROLE igas_console LOGIN'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'igas_console')\gexec
SELECT 'CREATE DATABASE igas_console OWNER igas_console'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'igas_console')\gexec
SQL
echo "    Rol y base creados. FALTA ponerle contrasena (no se genera aqui a proposito):"
echo "      sudo -u postgres psql -c \"ALTER ROLE igas_console PASSWORD 'ELIGE_UNA';\""

echo "==> Plantilla del archivo de entorno"
if [ ! -f /etc/zia-console/env ]; then
    cat > /etc/zia-console/env <<'ENVEOF'
# Secretos de produccion. NO versionar. Formato EnvironmentFile de systemd:
# CLAVE=valor, sin 'export', sin comillas alrededor del valor completo.
ConnectionStrings__Default=Host=127.0.0.1;Port=5432;Database=igas_console;Username=igas_console;Password=CAMBIAME

App__SelfUrl=https://api.ziacentro.mx
App__AngularUrl=https://console.ziacentro.mx
App__CorsOrigins=https://console.ziacentro.mx
App__RedirectAllowedUrls=https://console.ziacentro.mx
App__DataProtectionKeysPath=/var/lib/zia-console/keys

AuthServer__Authority=https://api.ziacentro.mx
AuthServer__RequireHttpsMetadata=true
AuthServer__CertificatePassPhrase=CAMBIAME
StringEncryption__DefaultPassPhrase=CAMBIAME

# El DbMigrator siembra los RedirectUris del cliente desde ESTAS variables
# (OpenIddictDataSeedContributor). Si no apuntan al dominio real, el login falla
# con 'invalid redirect_uri' aunque todo lo demas este bien.
OpenIddict__Applications__IgasConsole_App__ClientId=IgasConsole_App
OpenIddict__Applications__IgasConsole_App__RootUrl=https://console.ziacentro.mx
OpenIddict__Applications__IgasConsole_Swagger__ClientId=IgasConsole_Swagger
OpenIddict__Applications__IgasConsole_Swagger__RootUrl=https://api.ziacentro.mx

# Brevo: correo transaccional y SMS. La API key se genera en Brevo > SMTP & API.
# El remitente tiene que ser de un dominio VERIFICADO en Brevo (SPF/DKIM), si no
# los correos acaban en spam o Brevo los rechaza.
Brevo__ApiKey=CAMBIAME
# DuckDNS no deja publicar los registros SPF/DKIM que Brevo pide para verificar un
# dominio: el remitente tiene que ser una dirección verificada una a una en Brevo
# (Senders > Add a sender) o un dominio propio verificado.
Brevo__SenderEmail=CAMBIAME
Brevo__SenderName=Igas
# Remitente de SMS: alfanumerico de hasta 11 caracteres o un numero.
Brevo__SmsSender=Igas
# Escritorios remotos (MeshCentral). Solo se escribe si este archivo no existía.
# En un servidor ya instalado hay que añadir las claves a mano. Ver docs/escritorios-remotos.md.
# Escritorios__MeshCentral__Url=https://mesh.ziacentro.mx
# Escritorios__MeshCentral__UrlInterna=http://127.0.0.1:4430
# Escritorios__MeshCentral__User=admin
# Escritorios__MeshCentral__LoginTokenKey=
# Escritorios__MeshCentral__DeviceGroup=
ENVEOF
    chmod 600 /etc/zia-console/env
    chown root:root /etc/zia-console/env
    echo "    Creado /etc/zia-console/env — EDITALO antes de desplegar."
else
    echo "    Ya existe /etc/zia-console/env; no se toca."
fi

echo "==> systemd"
cp zia-console.service zia-console-migrator.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable zia-console
echo "    Servicio habilitado (aun no arranca: no hay nada en current/)."

echo "==> Copias de seguridad"
# Copia diaria de la base (03:30). Faltaba aqui y el primer despliegue se quedo sin copias:
# el panel lo avisaba («Sin informacion de copias de seguridad»). La copia FUERA del
# servidor necesita ademas /etc/zia-console/backup-remoto.{sh,env} (ver deploy/README.md).
install -m 755 -o root -g root zia-console-backup.sh /usr/local/bin/zia-console-backup.sh
cp zia-console-backup.service zia-console-backup.timer /etc/systemd/system/
mkdir -p /var/backups/zia-console /var/lib/zia-console /var/cache/restic
systemctl daemon-reload
systemctl enable --now zia-console-backup.timer

echo "==> nginx"
cp nginx.conf /etc/nginx/sites-available/zia-console
ln -sf /etc/nginx/sites-available/zia-console /etc/nginx/sites-enabled/zia-console
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx

echo "==> Permiso de despliegue"
# El workflow entra como $USUARIO_DEPLOY y necesita ejecutar SOLO deploy.sh con sudo.
cat > /etc/sudoers.d/zia-console <<SUDOEOF
$USUARIO_DEPLOY ALL=(root) NOPASSWD: $RAIZ/deploy.sh
SUDOEOF
chmod 440 /etc/sudoers.d/zia-console
visudo -c

echo "==> Swap"
# Muchos VPS vienen sin swap. Sin ella, un pico de memoria durante una migracion hace
# que el OOM killer mate la API, y en el log solo queda un "Killed" sin explicacion.
if swapon --show | grep -q .; then
    echo "    Ya hay swap; no se toca."
else
    fallocate -l 2G /swapfile
    chmod 600 /swapfile
    mkswap /swapfile >/dev/null
    swapon /swapfile
    grep -q "^/swapfile" /etc/fstab || echo "/swapfile none swap sw 0 0" >> /etc/fstab
    # Por defecto swappiness=60: el kernel manda a swap paginas que aun se usan y la
    # API se vuelve lenta sin motivo. 10 = usar swap solo bajo presion real.
    echo "vm.swappiness=10" > /etc/sysctl.d/99-swappiness.conf
    sysctl -q -p /etc/sysctl.d/99-swappiness.conf
    echo "    2G activos."
fi

echo "==> Retencion del journal"
# Sin esto journald solo tiene limites POR TAMANO (10% del disco) y ninguno por tiempo: la
# ventana de historial acaba dependiendo de cuanto ruido haya hecho la maquina, y no se sabe
# cual es. Ver los comentarios de journald-zia-console.conf.
install -d /etc/systemd/journald.conf.d
install -m 644 "$(dirname "$0")/journald-zia-console.conf" \
    /etc/systemd/journald.conf.d/zia-console.conf
systemctl restart systemd-journald
echo "    30 dias, tope 3G."

echo "==> Cortafuegos"
ufw allow OpenSSH
ufw allow 'Nginx Full'
ufw --force enable

cat <<'FIN'

=========================================================================
Falta por hacer a mano (necesita tu dominio y tus claves):

 1. Editar /etc/zia-console/env  (contrasenas y dominios reales)
 2. Poner la contrasena del rol de base de datos:
      sudo -u postgres psql -c "ALTER ROLE igas_console PASSWORD '...';"
 3. Revisar los server_name de /etc/nginx/sites-available/zia-console
 4. Certificado de firma de OpenIddict (NO es el de TLS; sin el la API no arranca
    en Production porque *.pfx esta en .gitignore y no viaja en los artefactos):
      openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=zia-console"         -keyout /tmp/oi.key -out /tmp/oi.crt
      openssl pkcs12 -export -out /etc/zia-console/openiddict.pfx         -inkey /tmp/oi.key -in /tmp/oi.crt -passout pass:LA_MISMA_QUE_EN_env
      rm -f /tmp/oi.key /tmp/oi.crt && chmod 640 /etc/zia-console/openiddict.pfx
    La contrasena debe coincidir con AuthServer__CertificatePassPhrase de /etc/zia-console/env.
 5. Certificados TLS:
      apt-get install -y certbot python3-certbot-nginx
      certbot --nginx -d console.ziacentro.mx -d api.ziacentro.mx
 6. Autorizar la clave publica de despliegue en ~deploy/.ssh/authorized_keys
    (el usuario 'deploy' ya lo crea este script; solo falta la clave)
 7. Cargar los secretos del repositorio en GitHub (ver deploy/README.md)
=========================================================================
FIN
