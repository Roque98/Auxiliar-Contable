#!/usr/bin/env bash
# Antivirus para lo que suben los clientes: ClamAV (clamd) en el mismo VPS, escuchando SOLO en
# 127.0.0.1:3310. La API lo usa si /etc/auxiliar-contable/env trae Antivirus__Host (ver
# ClamAvEscaner). Ejecutar como root, una vez:  bash instalar-antivirus.sh
#
# ⚠️ clamd carga las firmas en memoria: ~1.2 GB de RAM residente. Este script se niega a seguir
# si el servidor no tiene holgura (RAM + swap), porque el OOM killer elegiría a la API o a ZIA.
set -euo pipefail

MINIMO_MB=${MINIMO_MB:-1500}
disponible=$(awk '/MemAvailable/ {m=$2} /SwapFree/ {s=$2} END {print int((m+s)/1024)}' /proc/meminfo)
echo "==> Memoria disponible (RAM + swap libre): ${disponible} MB"
if [ "$disponible" -lt "$MINIMO_MB" ]; then
    echo "!! Hacen falta al menos ${MINIMO_MB} MB libres para clamd. No se instala." >&2
    exit 1
fi

echo "==> Paquetes"
apt-get update
apt-get install -y clamav clamav-daemon

echo "==> Firmas (la primera descarga tarda unos minutos)"
systemctl stop clamav-freshclam || true
freshclam || true
systemctl enable --now clamav-freshclam

echo "==> clamd por TCP, solo en localhost"
CONF=/etc/clamav/clamd.conf
grep -q '^TCPSocket ' "$CONF" || echo 'TCPSocket 3310' >> "$CONF"
grep -q '^TCPAddr ' "$CONF" || echo 'TCPAddr 127.0.0.1' >> "$CONF"
# Mismo tope que la app (20 MB) con holgura; por encima clamd responde «size limit exceeded».
sed -i 's/^StreamMaxLength .*/StreamMaxLength 25M/' "$CONF"
grep -q '^StreamMaxLength ' "$CONF" || echo 'StreamMaxLength 25M' >> "$CONF"
systemctl enable clamav-daemon
systemctl restart clamav-daemon

echo "==> Esperando a que clamd cargue las firmas"
for _ in $(seq 1 60); do
    if printf 'zPING\0' | timeout 2 nc -q1 127.0.0.1 3310 2>/dev/null | grep -q PONG; then
        echo "    clamd responde."
        break
    fi
    sleep 5
done

echo "==> Prueba con EICAR (archivo de prueba estándar, inofensivo)"
eicar='X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*'
# Por archivo y no por stdin: «clamdscan --stream -» no lee la entrada estándar y la prueba fallaba
# con clamd funcionando bien (pasó en la primera instalación, 2026-10-25).
prueba=$(mktemp)
printf '%s' "$eicar" > "$prueba"
chmod 644 "$prueba"
if clamdscan --no-summary --fdpass "$prueba" 2>/dev/null | grep -q FOUND; then
    echo "    Detecta EICAR: OK."
else
    echo "!! clamd no detectó EICAR; revisa 'systemctl status clamav-daemon'." >&2
fi
rm -f "$prueba"

if ! grep -q '^Antivirus__Host=' /etc/auxiliar-contable/env; then
    printf '\n# Antivirus de documentos (deploy/instalar-antivirus.sh)\nAntivirus__Host=127.0.0.1\n' >> /etc/auxiliar-contable/env
    echo "==> Añadido Antivirus__Host al env. Reinicia la API: systemctl restart auxiliar-contable"
fi
