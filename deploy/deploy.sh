#!/usr/bin/env bash
# Activa lo que el workflow dejo en staging/. Se ejecuta EN EL SERVIDOR, con sudo.
#
# ⚠️ Vive en /usr/local/sbin/auxiliar-contable-deploy (de root, 755) y el workflow NO lo sube.
# Si el usuario de despliegue pudiera reescribir el script que ejecuta con sudo, su llave
# valdria root. Al cambiar este archivo, instalarlo a mano como root:
#   install -m 755 -o root -g root deploy/deploy.sh /usr/local/sbin/auxiliar-contable-deploy
#
# Estrategia: current/ es lo que sirven nginx y systemd. La version anterior se guarda
# en previous/, se intercambia, se reinicia y se comprueba la salud; si la API no
# responde sana, se revierte sola.
set -euo pipefail

RAIZ=/srv/auxiliar-contable
SERVICIO=auxiliar-contable
USUARIO_APP=auxiliar
USUARIO_DEPLOY=deploy-auxiliar
CERT=/etc/auxiliar-contable/openiddict.pfx
LOGS=/var/log/auxiliar-contable

cd "$RAIZ"

# staging/ se le cede a $USUARIO_APP mas abajo para que el migrator lea los artefactos,
# pero quien escribe ahi por rsync es $USUARIO_DEPLOY. Sin devolversela, el SIGUIENTE
# despliegue muere con "mkdir staging/api: Permission denied". Va en un trap para que
# se restaure tambien si esto aborta a medias.
restaurar_staging() {
    mkdir -p "$RAIZ/staging"
    chown -R "$USUARIO_DEPLOY:$USUARIO_DEPLOY" "$RAIZ/staging" || true
}
trap restaurar_staging EXIT

for d in staging/api staging/web staging/migrator; do
    if [ ! -d "$d" ]; then
        echo "Falta $d. ¿Fallo la subida?" >&2
        exit 1
    fi
done

# El certificado de firma de OpenIddict NO viaja en el repo (.gitignore ignora *.pfx)
# ni en los artefactos: vive en el servidor. Sin el, en Production la API lanza al
# arrancar (AddProductionEncryptionAndSigningCertificate).
if [ ! -f "$CERT" ]; then
    echo "Falta $CERT. Generalo antes de desplegar (ver deploy/README.md)." >&2
    exit 1
fi

chown -R "$USUARIO_APP:$USUARIO_APP" staging

# Serilog escribe en 'Logs/' relativo al content root, pero las units usan
# ProtectSystem=strict: /srv es de solo lectura. El enlace redirige la escritura a
# /var/log/auxiliar-contable, que si esta en ReadWritePaths.
mkdir -p "$LOGS/api" "$LOGS/migrator"
chown -R "$USUARIO_APP:$USUARIO_APP" "$LOGS"
ln -sfn "$LOGS/migrator" staging/migrator/Logs

echo "==> Aplicando migraciones y seed"
# Antes de intercambiar: si la migracion falla, la version vieja sigue sirviendo.
systemctl start --wait auxiliar-contable-migrator

echo "==> Rotando versiones"
rm -rf previous
[ -d current ] && mv current previous
mkdir -p current
mv staging/api current/api
mv staging/web current/web
rm -rf staging/migrator

cp "$CERT" current/api/openiddict.pfx
ln -sfn "$LOGS/api" current/api/Logs
chown -R "$USUARIO_APP:$USUARIO_APP" current
chmod 400 current/api/openiddict.pfx
chown "$USUARIO_APP:$USUARIO_APP" current/api/openiddict.pfx

echo "==> Reiniciando $SERVICIO"
systemctl restart "$SERVICIO"

echo "==> Comprobando salud"
for i in $(seq 1 20); do
    codigo=$(curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5030/health-status || true)
    if [ "$codigo" = "200" ]; then
        echo "    Sano. Despliegue completado."
        systemctl reload nginx
        exit 0
    fi
    sleep 3
done

echo "!! La API no arranco sana. Revirtiendo." >&2
if [ -d previous ]; then
    rm -rf current
    mv previous current
    systemctl restart "$SERVICIO"
    echo "   Revertido a la version anterior." >&2
else
    echo "   No hay version anterior a la que volver." >&2
fi
exit 1
