#!/usr/bin/env bash
# Comprueba que la copia SIRVE, no solo que se subió.
#
# Se baja el último snapshot DESDE R2 —no desde el disco local— y lo restaura en una
# base temporal. Es la única prueba que vale: un volcado puede subirse entero, superar
# `restic check` y aun así no restaurar (una versión de PostgreSQL incompatible, un
# volcado de una base que ya no tiene los roles que espera, un cifrado que no se abre).
#
# ⚠️ NUNCA toca la base de producción. Crea una base con nombre propio, restaura ahí y la
# borra al terminar, pase lo que pase.
set -euo pipefail

BASE_PRUEBA=igas_console_verificacion
CONFIG=/etc/zia-console/backup-remoto.env
ESTADO=/var/lib/zia-console/restore-check-status.json
TRABAJO=/var/lib/zia-console/verificacion

inicio=$(date +%s)
etapa="inicio"
tablas=0
filas_usuarios=0
mensaje=""

escribir_estado() {
    local codigo=$1
    local ok="false"
    [ "$codigo" = "0" ] && ok="true"
    mkdir -p "$(dirname "$ESTADO")"
    cat > "$ESTADO.tmp" <<EOF
{
  "lastRunUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "ok": $ok,
  "stage": "$etapa",
  "durationSeconds": $(( $(date +%s) - inicio )),
  "tablesRestored": $tablas,
  "usersRestored": $filas_usuarios,
  "message": "$mensaje"
}
EOF
    mv "$ESTADO.tmp" "$ESTADO"
    chmod 644 "$ESTADO"
}

limpiar() {
    # Se ejecuta pase lo que pase: una base de verificación olvidada ocuparía disco y
    # confundiría a cualquiera que mirase la lista de bases.
    sudo -u postgres dropdb --if-exists "$BASE_PRUEBA" >/dev/null 2>&1 || true
    rm -rf "$TRABAJO"
}
trap 'codigo=$?; limpiar; escribir_estado $codigo' EXIT

[ -r "$CONFIG" ] || { mensaje="no hay credenciales del destino remoto"; exit 1; }
# shellcheck source=/dev/null
set -a; . "$CONFIG"; set +a

if [ -n "${R2_ENDPOINT:-}" ]; then
    endpoint="${R2_ENDPOINT%/}"
else
    endpoint="https://${R2_ACCOUNT_ID}.r2.cloudflarestorage.com"
fi
# R2_PREFIX (opcional): una carpeta dentro del bucket. Permite compartir bucket y token con
# otra app del mismo servidor (abt-template) sin mezclar repositorios: el suyo vive en la
# raíz y este en <bucket>/<prefijo>; restic solo mira dentro de su propia ruta.
export RESTIC_REPOSITORY="s3:${endpoint}/${R2_BUCKET}${R2_PREFIX:+/${R2_PREFIX}}"
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY RESTIC_PASSWORD
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-auto}"
export RESTIC_CACHE_DIR="${RESTIC_CACHE_DIR:-/var/cache/restic}"
# Ver la nota larga en backup-remoto.sh: Go no lee /etc/gai.conf, y el token de R2 está
# limitado a la IPv4 del VPS.
export GODEBUG="${GODEBUG:+$GODEBUG,}netdns=cgo"
mkdir -p "$RESTIC_CACHE_DIR"

rm -rf "$TRABAJO"; mkdir -p "$TRABAJO"

etapa="descarga"
echo "==> Bajando el último snapshot desde el destino remoto"
restic restore latest --tag igas-console --target "$TRABAJO"

dump=$(find "$TRABAJO" -name '*.dump' | head -1)
[ -n "$dump" ] || { mensaje="el snapshot no contiene ningun .dump"; exit 1; }
echo "    recuperado $(basename "$dump") ($(du -h "$dump" | cut -f1))"

etapa="restauracion"
echo "==> Restaurando en $BASE_PRUEBA"
sudo -u postgres dropdb --if-exists "$BASE_PRUEBA"
sudo -u postgres createdb "$BASE_PRUEBA"
# El usuario postgres tiene que poder LEER el archivo donde está; por eso se copia a un
# sitio accesible en vez de restaurar desde el directorio de trabajo, que es 700.
copia=/tmp/verificacion.dump
cp "$dump" "$copia"; chmod 644 "$copia"
sudo -u postgres pg_restore -d "$BASE_PRUEBA" "$copia"
rm -f "$copia"

etapa="comprobacion"
echo "==> Comprobando el contenido"
tablas=$(sudo -u postgres psql -d "$BASE_PRUEBA" -tAc \
    "SELECT count(*) FROM pg_tables WHERE schemaname='public';")
filas_usuarios=$(sudo -u postgres psql -d "$BASE_PRUEBA" -tAc \
    "SELECT count(*) FROM \"AbpUsers\";")

# Una base restaurada vacía es el fallo más traicionero: pg_restore puede terminar con
# éxito sobre un volcado sin datos. Se exige señal de vida real.
if [ "$tablas" -lt 20 ] || [ "$filas_usuarios" -lt 1 ]; then
    mensaje="restauro pero sin contenido creible: $tablas tablas, $filas_usuarios usuarios"
    echo "!! $mensaje" >&2
    exit 1
fi

mensaje="ok"
echo "    OK: $tablas tablas, $filas_usuarios usuario(s)"
etapa="completado"
