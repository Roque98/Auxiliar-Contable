#!/usr/bin/env bash
# Sube la copia recién hecha FUERA del servidor, a Cloudflare R2, con restic.
#
# Lo llama auxiliar-contable-backup.sh pasándole la ruta del volcado como $1. Se instala en
# /etc/auxiliar-contable/backup-remoto.sh (modo 700, root): esa ruta es la que busca el script
# de copia, y mientras no exista cada ejecución avisa en el log.
#
# ¿Por qué restic y no un 'rclone copy' a secas?
#   - CIFRA. El volcado lleva hashes de contraseñas, hashes de claves de API, los chats de
#     Telegram vinculados, las sesiones y las fotos de perfil. Subir eso en claro a un
#     bucket es mover el problema, no resolverlo.
#   - Deduplica y versiona: cada día es un snapshot, no un archivo suelto que hay que
#     rotar a mano en el destino.
#   - 'restic check' verifica el repositorio remoto de verdad, que es la diferencia entre
#     tener copias y creer que las tienes.
#
# ⚠️ LA CONTRASEÑA DEL REPOSITORIO NO PUEDE VIVIR SOLO AQUÍ. Si el único sitio donde está
# es este servidor, perderlo significa quedarse con copias que no se pueden descifrar —
# exactamente el escenario del que protegen. Va también en un gestor de contraseñas.
set -euo pipefail

CONFIG=/etc/auxiliar-contable/backup-remoto.env
RETENCION_DIARIA=14
RETENCION_SEMANAL=8
RETENCION_MENSUAL=6

volcado="${1:?Falta la ruta del volcado}"

if [ ! -r "$CONFIG" ]; then
    echo "!! No se puede leer $CONFIG. Sin credenciales no hay copia fuera del servidor." >&2
    exit 1
fi
# shellcheck source=/dev/null
. "$CONFIG"

: "${R2_BUCKET:?Falta R2_BUCKET en $CONFIG}"
: "${AWS_ACCESS_KEY_ID:?Falta AWS_ACCESS_KEY_ID en $CONFIG}"
: "${AWS_SECRET_ACCESS_KEY:?Falta AWS_SECRET_ACCESS_KEY en $CONFIG}"
: "${RESTIC_PASSWORD:?Falta RESTIC_PASSWORD en $CONFIG}"

# restic habla S3 y R2 es compatible.
#
# El endpoint se puede dar ENTERO (R2_ENDPOINT) o dejar que se componga a partir del id de
# cuenta. Se admite entero porque componerlo falla en dos casos reales y el error que da es
# el mismo e igual de opaco —«tls: handshake failure»—, sin pista de cuál de los dos es:
#   * el id de cuenta no es el que parece (en Cloudflare el id de ZONA tiene la misma
#     pinta: 32 caracteres hexadecimales, y están a un clic de distancia);
#   * el bucket se creó con jurisdicción europea, y entonces el host lleva '.eu' en medio
#     (<cuenta>.eu.r2.cloudflarestorage.com).
# La página del bucket muestra su endpoint S3 tal cual: copiarlo de ahí evita adivinar.
if [ -n "${R2_ENDPOINT:-}" ]; then
    endpoint="${R2_ENDPOINT%/}"
else
    : "${R2_ACCOUNT_ID:?Falta R2_ACCOUNT_ID (o R2_ENDPOINT) en $CONFIG}"
    endpoint="https://${R2_ACCOUNT_ID}.r2.cloudflarestorage.com"
fi

# R2_PREFIX (opcional): una carpeta dentro del bucket. Permite compartir bucket y token con
# otra app del mismo servidor (abt-template) sin mezclar repositorios: el suyo vive en la
# raíz y este en <bucket>/<prefijo>; restic solo mira dentro de su propia ruta.
export RESTIC_REPOSITORY="s3:${endpoint}/${R2_BUCKET}${R2_PREFIX:+/${R2_PREFIX}}"
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY RESTIC_PASSWORD
# R2 no usa regiones, pero el cliente S3 exige uno: 'auto' es el que documenta Cloudflare.
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-auto}"

# ⚠️ La caché NO puede quedarse donde restic la pone por defecto (~/.cache/restic).
# auxiliar-contable-backup.service corre con ProtectHome=true, así que /root no existe para el
# proceso y restic falla al arrancar. No se ve probando el script a mano —ahí /root sí está—
# sino solo cuando lo lanza systemd, que es la única forma en que corre de verdad.
# El directorio está declarado en ReadWritePaths de la unidad.
export RESTIC_CACHE_DIR="${RESTIC_CACHE_DIR:-/var/cache/restic}"
mkdir -p "$RESTIC_CACHE_DIR"

# ⚠️⚠️ SALIR POR IPv4. El token de R2 está limitado a la IPv4 fija del VPS, y este
# servidor tiene IPv6: saliendo por ella, Cloudflare ve otra dirección, el filtro no casa
# y RECHAZA EL TOKEN ENTERO — con «403 AccessDenied» en toda operación, que es el mismo
# error exacto que daría un secreto incorrecto. Indistinguible desde fuera.
#
# Poner `precedence ::ffff:0:0/96 100` en /etc/gai.conf NO basta: restic es un programa Go
# y Go trae su propio selector de direcciones, que no lee gai.conf. Hay que empujarlo al
# resolutor del sistema. Comprobado: sin esto «Access Denied», con esto lista los snapshots.
export GODEBUG="${GODEBUG:+$GODEBUG,}netdns=cgo"

raiz=$(dirname "$(dirname "$volcado")")   # .../diario/x.dump -> ...
roles="$raiz/roles.sql"

echo "==> Inicializando el repositorio si hace falta"
# 'cat config' es la comprobación barata de "¿existe y puedo abrirlo?". Si no existe se
# crea; si existe y la contraseña no vale, init fallaría y el error se vería aquí.
if ! restic cat config >/dev/null 2>&1; then
    restic init
fi

echo "==> Subiendo $(basename "$volcado")"
# Se suben el volcado Y los roles: sin los roles, restaurar en una máquina limpia falla
# por permisos y la copia sirve de mucho menos de lo que parece.
#
# Y los DOCUMENTOS de los clientes: viven en disco, no en la base (CLAUDE.md §1.2.10), así
# que el volcado no los lleva. Van ya cifrados por la aplicación; restic los vuelve a cifrar.
# ⚠️ Sin Documentos__ClaveDeCifrado (guardada también fuera del servidor) no se pueden leer.
DOCUMENTOS=/var/lib/auxiliar-contable/documentos
rutas=("$volcado")
if [ -r "$roles" ]; then
    rutas+=("$roles")
else
    echo "!! No se encontró $roles; se sube sin los roles." >&2
fi
if [ -d "$DOCUMENTOS" ]; then
    rutas+=("$DOCUMENTOS")
fi
restic backup --tag auxiliar-contable --host auxiliar-contable "${rutas[@]}"

echo "==> Aplicando retención"
# --prune borra de verdad los datos que ya no referencia ningún snapshot. Sin él la
# retención solo quita etiquetas y el bucket crece para siempre.
restic forget \
    --tag auxiliar-contable \
    --keep-daily "$RETENCION_DIARIA" \
    --keep-weekly "$RETENCION_SEMANAL" \
    --keep-monthly "$RETENCION_MENSUAL" \
    --prune

echo "==> Verificando el repositorio"
# Una copia que no se puede leer no es una copia. --read-data-subset descarga una parte y
# comprueba los hashes de verdad: 'check' a secas solo mira los metadatos, que es
# justamente lo que nunca se corrompe.
restic check --read-data-subset=5%

cuantos=$(restic snapshots --tag auxiliar-contable --json | grep -o '"short_id"' | wc -l)
echo "    OK: $cuantos snapshots en el destino"

# Se lo dejamos al script padre, que es quien escribe el estado que lee la aplicación.
mkdir -p /var/lib/auxiliar-contable
echo "$cuantos" > /var/lib/auxiliar-contable/remote-snapshots
chmod 644 /var/lib/auxiliar-contable/remote-snapshots
