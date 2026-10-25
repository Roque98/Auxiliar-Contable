#!/usr/bin/env bash
# Deja SSH solo con clave y monta fail2ban. Ejecutar como root:  bash endurecer-ssh.sh
#
# NO va dentro de setup-servidor.sh a proposito: si se ejecuta en una maquina donde
# todavia entras por contrasena, te quedas fuera. De ahi la comprobacion de abajo.
set -euo pipefail

USUARIO_DEPLOY=${USUARIO_DEPLOY:-deploy}

echo "==> Comprobando que hay por donde entrar despues"
# Desactivar la contrasena sin tener una clave autorizada es la forma mas rapida de
# perder un servidor. Se aborta antes de tocar nada.
claves_root=0
[ -s /root/.ssh/authorized_keys ] && claves_root=$(grep -c "^ssh-" /root/.ssh/authorized_keys || true)
if [ "$claves_root" -eq 0 ]; then
    cat >&2 <<'AVISO'
!! /root/.ssh/authorized_keys esta vacio o no existe.
   Si desactivo la contrasena ahora, te quedas fuera del servidor.
   Autoriza primero tu clave publica:
     ssh-copy-id -i ~/.ssh/tu_clave.pub root@ESTE_SERVIDOR
AVISO
    exit 1
fi
echo "    OK: $claves_root clave(s) autorizada(s) para root"

echo "==> Configuracion de sshd"
# Numerado 01 A PROPOSITO: OpenSSH se queda con el PRIMER valor que encuentra para
# cada opcion, y los drop-ins se leen por orden alfabetico. cloud-init reescribe
# 50-cloud-init.conf con "PasswordAuthentication yes" al aprovisionar; desde el 01
# no puede ganarnos.
cat > /etc/ssh/sshd_config.d/01-endurecimiento.conf <<'SSHEOF'
PasswordAuthentication no

# Sin esto, PAM sigue ofreciendo contrasena por keyboard-interactive y lo de arriba
# no sirve de nada. Es el fallo clasico al endurecer SSH.
KbdInteractiveAuthentication no

PermitRootLogin prohibit-password
PubkeyAuthentication yes
SSHEOF
chmod 600 /etc/ssh/sshd_config.d/01-endurecimiento.conf

# Validar ANTES de recargar: un error de sintaxis aqui es lo que deja a la gente fuera.
sshd -t
systemctl reload ssh
echo "    Aplicado. Valores efectivos:"
sshd -T | grep -E "^(permitrootlogin|passwordauthentication|kbdinteractiveauthentication)" | sed 's/^/      /'

echo "==> fail2ban"
DEBIAN_FRONTEND=noninteractive apt-get install -y fail2ban >/dev/null

# Filtro de la jail de abuso HTTP: banea por 429, no por 401/403/404 sueltos. La API
# (.NET, rate limiting por IP) ya decidio que esa IP es abuso cuando le devuelve 429;
# aqui solo hace falta ver el patron repetirse para cortarla en el firewall, en vez de
# dejarla seguir golpeando la API para siempre. Un 401/403/404 aislado es el ruido
# normal de cualquier servidor publico (mismo criterio que MetricsCollectorWorker.cs
# al decidir que NO avisa por esos codigos) y banear por eso generaria falsos positivos.
cat > /etc/fail2ban/filter.d/auxiliar-contable-abuso.conf <<'FILTEREOF'
[Definition]
failregex = ^<HOST> -.*"(?:GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS) \S+ HTTP/\d\.\d" 429
ignoreregex =
FILTEREOF

cat > /etc/fail2ban/jail.local <<'F2BEOF'
[DEFAULT]
# Ubuntu 24.04 no escribe /var/log/auth.log: todo va al journal de systemd. Sin esto
# fail2ban vigila un fichero que no existe y no banea a nadie, en silencio.
backend = systemd
bantime  = 1h
findtime = 10m
maxretry = 4
ignoreip = 127.0.0.1/8 ::1

[sshd]
enabled = true

[auxiliar-contable-abuso]
enabled  = true
# ESTE jail SI lee un fichero (el access.log de nginx), no el journal: el backend
# systemd del DEFAULT es solo para sshd. Sin este override, fail2ban intentaria
# buscar un journalmatch que no existe y esta jail nunca vigilaria nada, en silencio.
backend  = auto
filter   = auxiliar-contable-abuso
logpath  = /var/log/nginx/access.log
# 3 y no 4 (el maxretry del DEFAULT): a esto ya lo filtro el rate limiter de la API,
# asi que cada 429 que llega aqui es una IP que YA fue avisada y sigue insistiendo.
maxretry = 3
# IMPRESCINDIBLE. El [DEFAULT] de /etc/fail2ban/jail.conf trae 'port = 0:65535', asi
# que sin esta linea un baneo por abusar del HTTP cierra TODOS los puertos, SSH
# incluido: un falso positivo (o una prueba tuya) te deja fuera del servidor. Quien
# abusa de la API se queda sin API, no sin poder administrar la maquina.
port     = http,https
F2BEOF
systemctl enable fail2ban >/dev/null
# restart y no "enable --now": si el script se re-ejecuta sobre un servidor que YA
# tenia fail2ban activo (p. ej. para sumar la jail de abuso mas tarde), "--now" no
# recarga nada porque el servicio ya esta arriba y el jail.local nuevo se queda sin
# aplicar hasta el proximo reinicio del servidor.
systemctl restart fail2ban
echo "    Activo."

cat <<'FIN'

=========================================================================
Antes de cerrar esta sesion, ABRE OTRA y comprueba que entras con tu clave.
Si algo fuera mal, Contabo (y casi cualquier proveedor) da consola web por
VNC, que no pasa por SSH.

  fail2ban-client status sshd                # a quien ha baneado por SSH
  fail2ban-client status auxiliar-contable-abuso  # a quien ha baneado por abusar de la API
  fail2ban-client set sshd unbanip 1.2.3.4
=========================================================================
FIN
