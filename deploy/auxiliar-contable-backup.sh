#!/usr/bin/env bash
# Copia de seguridad diaria de la base principal. La lanza auxiliar-contable-backup.timer.
#
# Formato custom (-Fc): comprimido, y pg_restore puede sacar tablas sueltas sin
# tragarse el volcado entero. Un .sql plano no permite eso.
set -euo pipefail

BASE=auxiliar_contable
RAIZ=/var/backups/auxiliar-contable
RETENCION_DIARIA=14   # dias
RETENCION_SEMANAL=8   # semanas
GANCHO_REMOTO=/etc/auxiliar-contable/backup-remoto.sh
ESTADO=/var/lib/auxiliar-contable/backup-status.json

fecha=$(date +%F_%H%M%S)
inicio=$(date +%s)

# --- Estado para la aplicación -------------------------------------------------
# La copia corre FUERA de la aplicación, así que sin esto la aplicación no puede
# saber si existe ni cuándo fue la última. Se escribe SIEMPRE, también al fallar:
# el caso que hay que detectar es justamente "lleva semanas rota y nadie lo sabe",
# y para eso el archivo tiene que contar el fallo, no dejar de actualizarse.
etapa="inicio"
tablas=0
bytes=0
snapshots=0
remoto="no-configurado"

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
  "dumpBytes": $bytes,
  "tables": $tablas,
  "remote": "$remoto",
  "remoteSnapshots": $snapshots
}
EOF
    # Movimiento atómico: si la aplicación lee justo mientras se escribe, no puede
    # encontrarse un JSON a medias.
    mv "$ESTADO.tmp" "$ESTADO"
    # Legible por el usuario 'auxiliar', que es quien corre la API. No lleva secretos.
    chmod 644 "$ESTADO"
}
trap 'escribir_estado $?' EXIT

mkdir -p "$RAIZ/diario" "$RAIZ/semanal"
chmod 700 "$RAIZ"

destino="$RAIZ/diario/${BASE}_${fecha}.dump"

etapa="volcado"
echo "==> Volcando $BASE"
# A un temporal primero: si pg_dump muere a medias, no queda un .dump truncado con
# pinta de copia buena.
sudo -u postgres pg_dump -Fc "$BASE" > "$destino.parcial"
mv "$destino.parcial" "$destino"

etapa="verificacion"
echo "==> Verificando el volcado"
# Una copia que no se puede leer no es una copia. pg_restore --list falla si el
# archivo esta truncado o corrupto, y cuesta milisegundos.
if ! pg_restore --list "$destino" > /dev/null 2>&1; then
    echo "!! El volcado no se puede leer. Se borra para no dar falsa seguridad." >&2
    rm -f "$destino"
    exit 1
fi
tablas=$(pg_restore --list "$destino" | grep -c "TABLE DATA" || true)
bytes=$(stat -c %s "$destino")
echo "    OK: $(du -h "$destino" | cut -f1), $tablas tablas con datos"

etapa="roles"
# Los roles no van en el volcado de una sola base: sin ellos, restaurar en una maquina
# limpia falla por permisos ("role auxiliar_contable does not exist").
sudo -u postgres pg_dumpall --globals-only > "$RAIZ/roles.sql"

etapa="rotacion"
echo "==> Rotacion"
# Los domingos la copia se guarda ademas como semanal, para tener profundidad sin
# acumular 90 volcados diarios.
if [ "$(date +%u)" = "7" ]; then
    cp "$destino" "$RAIZ/semanal/${BASE}_${fecha}.dump"
fi
find "$RAIZ/diario"  -name "*.dump" -mtime +$RETENCION_DIARIA -delete
find "$RAIZ/semanal" -name "*.dump" -mtime +$((RETENCION_SEMANAL * 7)) -delete
echo "    diarias: $(ls -1 "$RAIZ/diario" | wc -l), semanales: $(ls -1 "$RAIZ/semanal" | wc -l)"

chmod -R go-rwx "$RAIZ"

# Una copia en el mismo disco que la base no protege de que el disco (o el VPS) se
# pierda. Si existe el gancho, se le pasa el volcado recien hecho para subirlo fuera.
if [ -x "$GANCHO_REMOTO" ]; then
    etapa="remoto"
    echo "==> Enviando fuera del servidor"
    "$GANCHO_REMOTO" "$destino"
    remoto="ok"
    # El gancho deja aqui cuantos snapshots hay en el destino.
    [ -r /var/lib/auxiliar-contable/remote-snapshots ] && \
        snapshots=$(cat /var/lib/auxiliar-contable/remote-snapshots)
else
    remoto="sin-gancho"
    echo "!! Sin copia fuera del servidor: no existe $GANCHO_REMOTO" >&2
fi

etapa="completado"
