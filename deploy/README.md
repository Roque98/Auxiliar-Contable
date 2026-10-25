# Despliegue a VPS

Pipeline sin Docker: GitHub Actions compila y publica, `rsync` sube los artefactos,
y systemd + nginx los sirven. La base de datos es PostgreSQL en el propio servidor.

```
GitHub Actions                         VPS (Ubuntu)
  build + tests  ──> publicado/  ─rsync─>  /srv/auxiliar-contable/staging/
                                              │
                                              ├─ migrator ─> systemctl start --wait auxiliar-contable-migrator
                                              └─ deploy.sh ─> intercambia current/ y reinicia
                                                              (revierte solo si no arranca sano)
```

## Archivos

| Archivo | Dónde va | Qué hace |
|---|---|---|
| `setup-servidor.sh` | se ejecuta una vez, como root | Instala runtime, nginx, PostgreSQL, crea usuario y directorios |
| `auxiliar-contable.service` | `/etc/systemd/system/` | La API bajo systemd, en `127.0.0.1:5030` |
| `auxiliar-contable-migrator.service` | `/etc/systemd/system/` | Unidad oneshot que aplica migraciones y seed |
| `nginx.conf` | `/etc/nginx/sites-available/auxiliar-contable` | TLS, proxy inverso a la API y estáticos del Angular |
| `deploy.sh` | `/usr/local/sbin/auxiliar-contable-deploy` (de root; el workflow NO lo sube: se instala a mano al cambiarlo) | Migra, intercambia versiones, reinicia y verifica |
| `endurecer-ssh.sh` | se ejecuta una vez, como root | Deja SSH solo con clave y monta fail2ban |

## Puesta en marcha (una vez)

1. Crea el VPS con Ubuntu 22.04 o 24.04 y apunta los DNS:
   `TU-DOMINIO.com` y `api.TU-DOMINIO.com` → IP del servidor.
2. Crea el usuario de despliegue y sube tu clave pública:
   ```bash
   adduser --disabled-password --gecos "" deploy
   mkdir -p /home/deploy/.ssh && chmod 700 /home/deploy/.ssh
   # pega la clave pública en /home/deploy/.ssh/authorized_keys
   chown -R deploy:deploy /home/deploy/.ssh && chmod 600 /home/deploy/.ssh/authorized_keys
   ```
3. Copia esta carpeta al servidor y ejecuta el bootstrap:
   ```bash
   scp -r deploy/ root@IP:/tmp/
   ssh root@IP "cd /tmp/deploy && bash setup-servidor.sh"
   ```
4. Haz los 7 pasos manuales que el script lista al terminar (dominios, contraseñas,
   certificados TLS).

5. Endurece el acceso, **una vez tengas tu clave autorizada**:
   ```bash
   ssh root@IP "cd /tmp/deploy && bash endurecer-ssh.sh"
   ```
   Deja SSH solo con clave y activa fail2ban. Va aparte de `setup-servidor.sh` porque
   ejecutarlo antes de tener la clave puesta te deja fuera del servidor; el script lo
   comprueba y aborta si `/root/.ssh/authorized_keys` está vacío.

   Ojo con el orden de los ficheros de sshd: OpenSSH se queda con el **primer** valor
   que encuentra y cloud-init deja un `50-cloud-init.conf` con
   `PasswordAuthentication yes`. Por eso el nuestro se llama `01-endurecimiento.conf`.

   También monta una segunda jail, `auxiliar-contable-abuso`, que lee el `access.log` de
   nginx y banea a nivel de firewall las IPs que reciben varios `429` seguidos — el
   rate limiting de la API (ver `AuxiliarContable.HttpApi.Host/Monitoring/RateLimiting*`)
   ya identificó esa IP como abuso; esta jail hace que además deje de poder conectar,
   en vez de seguir golpeando la API para siempre. No banea por `401`/`403`/`404`
   sueltos: eso es tráfico normal de cualquier servidor público (mismo criterio que el
   monitoreo aplica para no avisar por eso).

## En el VPS de ZIA (convive con otras aplicaciones)

Auxiliar contable se despliega como una aplicación **más** en el servidor de ZIA, que ya sirve
abt-template, ZIA-Console y el chatbot de ZIA. No comparten nada que se pise:

| | ZIA-Console | Auxiliar contable |
|---|---|---|
| Carpeta | `/srv/zia-console` | `/srv/auxiliar-contable` |
| Puerto interno (Kestrel) | `127.0.0.1:5010` | `127.0.0.1:5030` (5000 = abt-template, 5020 = chatbot) |
| Usuario de servicio | `zia` | `auxiliar` |
| Base de datos / rol | `igas_console` (conserva el nombre viejo) | `auxiliar_contable` |
| Secretos | `/etc/zia-console/` | `/etc/auxiliar-contable/` |
| sudoers | `/etc/sudoers.d/zia-console` | `/etc/sudoers.d/auxiliar-contable` |
| Sitio nginx | `sites-available/zia-console` | `sites-available/auxiliar-contable` |
| Certificado | el de ziacentro.mx | uno propio para `contable` y `api-contable` |

⚠️ Desde aquí **no se toca nada de ZIA**: ni su sitio nginx, ni su certificado, ni sus servicios.
Lo que es del servidor (snippets de Cloudflare, OpenObserve, fail2ban, swap, SSH) lo mantiene
ZIA-Console; esta app solo lo usa.

El usuario de despliegue es **propio**, `deploy-auxiliar`, y NO el `deploy` de ZIA: con el
compartido, la llave de este repo podía subir archivos al staging de ZIA y ejecutar su
activación. Su sudoers solo permite `/usr/local/sbin/auxiliar-contable-deploy`, que es de root:
si el script estuviera en una carpeta que el usuario puede escribir (como el `deploy.sh` de ZIA,
que el workflow sube a `/srv/zia-console/`), reescribirlo daría root a quien tenga la llave.
La llave es **solo** de este repositorio (`github-actions@Roque98/Auxiliar-Contable`).

Qué cambia respecto a la puesta en marcha de arriba:

- `setup-servidor.sh` se puede correr tal cual: los paquetes, el swap, el cortafuegos y la
  retención del journal ya están y el script no los duplica. **Ojo**: borra
  `sites-enabled/default` de nginx, que ya no existía; no toca el sitio de abt-template.
- `endurecer-ssh.sh` **no** hace falta: es del servidor, no de la aplicación, y ya se corrió.
- Antes del primer despliegue, mirar la memoria (`free -h`): son dos APIs .NET y un
  PostgreSQL en la misma máquina.
- Dominios: `contable.ziacentro.mx` (app) y `api-contable.ziacentro.mx` (API): registros **A** en
  Cloudflare hacia la IP del VPS, **proxificados** (nube naranja) como el resto de ziacentro.mx: el
  origen solo acepta tráfico de Cloudflare (ver abajo). Certificado propio:
  `certbot certonly --webroot -w /var/www/acme -d contable.ziacentro.mx -d api-contable.ziacentro.mx`.

## Brevo (correo, SMS y WhatsApp)

La API key va en `/etc/auxiliar-contable/env` (`Brevo__ApiKey`), nunca en el repositorio.
Tres cosas de fuera que hacen falta para que los mensajes salgan de verdad:

1. **IPs autorizadas.** Si la cuenta tiene activado *Authorised IPs*, añadir la IPv4 **y**
   la IPv6 del VPS en <https://app.brevo.com/security/authorised_ips>. Sin eso Brevo
   responde `401 unrecognised IP address` aunque la clave sea buena.
2. **Remitente verificado.** `Brevo__SenderEmail` tiene que ser de un dominio verificado
   en Brevo (SPF y DKIM), o los correos acaban en spam o rechazados.
3. **WhatsApp** solo deja escribir primero con plantillas aprobadas por Meta; el texto
   libre solo llega si la persona escribió en las últimas 24 horas.

Sin `Brevo__ApiKey` la aplicación arranca igual y cada envío queda en el log como aviso.

## Claude (IA)

La API key de Anthropic va en `/etc/auxiliar-contable/env` como `Claude__ApiKey` (se crea en
<https://console.anthropic.com/settings/keys>; conviene una clave **solo para esta app** y un
**límite de gasto** en la consola de Anthropic). El modelo se cambia con `Claude__Modelo`
(por defecto `claude-haiku-5-5`). Tras editar el `env`: `sudo systemctl restart auxiliar-contable`.

Sin `Claude__ApiKey` la aplicación arranca igual; lo que necesite IA lanza «no configurado».
Ver `CLAUDE.md` §1.2.8.

## Esconder la puerta del superadministrador (opcional)

Por defecto el panel del host está en `https://TU-DOMINIO.com/` y el formulario de login
en `https://api.TU-DOMINIO.com/Account/Login`: las dos rutas por defecto de cualquier ABP,
y las dos primeras que prueba un escáner. Se mueven las dos a la vez añadiendo **una línea**
a `/etc/auxiliar-contable/env`:

```bash
Acceso__SegmentoDelHost=elige-algo-largo
```

```bash
systemctl restart auxiliar-contable
```

A partir de ahí:

| | |
|---|---|
| `https://TU-DOMINIO.com/` | «este espacio no está disponible» |
| `https://TU-DOMINIO.com/elige-algo-largo` | panel del host |
| `https://api.TU-DOMINIO.com/Account/Login` | 404 |
| `https://api.TU-DOMINIO.com/elige-algo-largo` | formulario de login |

Los espacios de inquilino (`/app/{nombre}`) **no cambian**.

> ⚠️ **Esto reduce ruido, no es una cerradura.** Frena el escaneo automático y evita que
> quien pase por el dominio se encuentre un panel de administración. No frena a quien vaya
> a por ti: el segmento se puede adivinar y quien construya un `/connect/authorize` válido
> lo recibe en la redirección. **Lo que protege esa cuenta es el segundo factor.**

**Para volver atrás**: borra la línea y reinicia. No hace falta desplegar nada — un valor
vacío o inválido devuelve la instalación a su comportamiento de siempre. Ese es el
interruptor de emergencia si te quedas fuera.

Reglas del valor: letras, dígitos, `-` y `_`; de 4 a 64 caracteres. Detalles y las trampas
que tiene detrás, en `CLAUDE.md` §6.98.

## Secretos del repositorio

En GitHub → Settings → Secrets and variables → Actions:

| Secreto | Valor |
|---|---|
| `VPS_HOST` | IP o hostname del servidor |
| `VPS_USER` | `deploy-auxiliar` |
| `VPS_SSH_KEY` | Clave **privada** de despliegue, completa, incluidas las líneas `BEGIN`/`END` |
| `VPS_KNOWN_HOSTS` | Salida de `ssh-keyscan TU_IP` |
| `VPS_DOMINIO` | `api-contable.ziacentro.mx` |

`VPS_KNOWN_HOSTS` no es opcional: sin él habría que usar
`StrictHostKeyChecking=no`, y entonces el deploy entregaría la clave privada a
cualquier servidor que respondiera en esa IP.

Genera una clave **dedicada al despliegue**, no reutilices tu clave personal:

```bash
ssh-keygen -t ed25519 -f ~/.ssh/abp_deploy -C "deploy auxiliar-contable" -N ""
```

## Desplegar

El workflow es **manual** de partida: Actions → *Deploy a VPS* → *Run workflow*.
Cuando le tengas confianza, descomenta el bloque `push:` de
`.github/workflows/deploy.yml` para que salga en cada merge a `main`.

## Volver atrás

`deploy.sh` revierte solo si la API no arranca sana. Para revertir a mano:

```bash
ssh deploy@IP
sudo systemctl stop auxiliar-contable
sudo rm -rf /srv/auxiliar-contable/current
sudo mv /srv/auxiliar-contable/previous /srv/auxiliar-contable/current
sudo systemctl start auxiliar-contable
```

Ojo: eso revierte el **código**, no la base de datos. Una migración que borra
columnas no se deshace sola — para eso hacen falta copias de seguridad.

## Copias de seguridad

Montadas con un timer de systemd (no `cron.daily`: deja rastro en `journalctl`,
recupera la ejecución perdida si el servidor estaba apagado y se puede lanzar a mano).

| Archivo | Dónde va |
|---|---|
| `auxiliar-contable-backup.sh` | `/usr/local/bin/` (modo 750) |
| `auxiliar-contable-backup.service` | `/etc/systemd/system/` |
| `auxiliar-contable-backup.timer` | `/etc/systemd/system/`, luego `systemctl enable --now` |

Cada noche a las 03:30 (±15 min) vuelca `auxiliar_contable` en formato custom a
`/var/backups/auxiliar-contable/diario/`, **comprueba que el volcado se puede leer**
(`pg_restore --list`; si no, lo borra en vez de dejar una copia falsa), guarda también
los roles en `roles.sql` y rota: 14 diarias y 8 semanales (las de los domingos).

```bash
systemctl list-timers auxiliar-contable-backup.timer   # cuándo toca la próxima
systemctl start auxiliar-contable-backup               # lanzar una a mano
journalctl -u auxiliar-contable-backup -n 20           # cómo fue la última
```

### Restaurar

Probado de verdad, no solo escrito. El volcado es `root:700`, así que el usuario
`postgres` no puede leerlo donde vive: hay que darle una copia suya primero, y ese es
el paso que hace fallar el `pg_restore` de manual.

```bash
DUMP=$(ls -t /var/backups/auxiliar-contable/diario/*.dump | head -1)
install -o postgres -g postgres -m 400 "$DUMP" /tmp/restaurar.dump

# Ensayo en una base desechable (recomendado antes de tocar la de verdad):
sudo -u postgres psql -c "CREATE DATABASE prueba_restauracion OWNER auxiliar_contable;"
sudo -u postgres pg_restore -d prueba_restauracion --no-owner --role=auxiliar_contable /tmp/restaurar.dump

# Sobre la base real: parar la API primero, o escribirá encima a media restauración.
sudo systemctl stop auxiliar-contable
sudo -u postgres psql -c "DROP DATABASE auxiliar_contable;"                     -c "CREATE DATABASE auxiliar_contable OWNER auxiliar_contable;"
sudo -u postgres pg_restore -d auxiliar_contable --no-owner --role=auxiliar_contable /tmp/restaurar.dump
sudo systemctl start auxiliar-contable
rm -f /tmp/restaurar.dump
```

En una máquina limpia, restaura antes los roles: `sudo -u postgres psql -f roles.sql`
(sin ellos falla con *role "auxiliar_contable" does not exist*).

### Fuera del servidor

Una copia en el mismo disco que la base no protege de perder el disco ni el VPS. El
script ejecuta `/etc/auxiliar-contable/backup-remoto.sh` pasándole la ruta del volcado recién
hecho. Mientras ese gancho no exista, cada ejecución lo avisa en el log.

El gancho de este repo (`backup-remoto.sh`) sube a **Cloudflare R2 con restic**.

| Archivo | Destino en el servidor |
|---|---|
| `backup-remoto.sh` | `/etc/auxiliar-contable/` (modo **700**, root) |
| `backup-remoto.env.example` | `/etc/auxiliar-contable/backup-remoto.env` (modo **600**), con los valores reales |

Hace falta `restic` instalado (`apt-get install restic`).

**Por qué restic y no un `rclone copy`:** el volcado lleva hashes de contraseñas, hashes de
claves de API, los chats de Telegram vinculados y las fotos de perfil. Subir eso en claro a
un bucket mueve el problema en vez de resolverlo. Además restic versiona (cada día es un
snapshot), aplica la retención en el destino y `restic check` verifica el repositorio
remoto de verdad.

⚠️ **`RESTIC_PASSWORD` no puede vivir solo en el servidor.** Si su único sitio es esta
máquina, perderla deja copias que nadie puede descifrar — justo el escenario del que
protegen. Va también en un gestor de contraseñas.

⚠️ **La caché de restic va en `/var/cache/restic`, declarada en `ReadWritePaths` de la
unidad.** `auxiliar-contable-backup.service` corre con `ProtectHome=true`, así que `/root` no
existe para el proceso y ahí es donde restic la pondría por defecto. Sin esa línea el
gancho falla **solo** cuando lo lanza systemd: probándolo a mano funciona, porque ahí
`/root` sí está.

Mirar el destino a mano:

```bash
set -a; . /etc/auxiliar-contable/backup-remoto.env; set +a
export RESTIC_REPOSITORY="s3:https://$R2_ACCOUNT_ID.r2.cloudflarestorage.com/$R2_BUCKET"
export RESTIC_CACHE_DIR=/var/cache/restic
restic snapshots          # qué hay subido
restic check              # ¿está sano el repositorio?
```

## Comprobaciones

```bash
systemctl status auxiliar-contable
journalctl -u auxiliar-contable -f          # logs en vivo
journalctl -u auxiliar-contable-migrator -n 50
curl -s localhost:5030/health-status
systemctl list-timers auxiliar-contable-backup.timer
```

## Cloudflare delante de *.ziacentro.mx (solo tráfico desde Cloudflare)

Todo ziacentro.mx (incluidos `contable` y `api-contable`) va proxificado por Cloudflare (SSL Full
strict). Lo de abajo es del SERVIDOR: lo instaló y lo mantiene ZIA-Console; este repo solo incluye los
snippets en su sitio nginx. En el origen:

- `/etc/nginx/conf.d/cloudflare-geo.conf` (generado): `geo $realip_remote_addr $desde_cloudflare` con los rangos oficiales de Cloudflare + 127.0.0.1/::1. Solo define una variable.
- `/etc/nginx/snippets/cloudflare-realip.conf` (generado): `set_real_ip_from` + `real_ip_header CF-Connecting-IP`. Se incluye **solo** en los `server{}` de *.ziacentro.mx, así `$remote_addr`, los logs, `limit_req` y `X-Real-IP`/`X-Forwarded-For` hacia los backends llevan la IP real del visitante.
- `/etc/nginx/snippets/cloudflare-solo.conf` (fijo): `if ($desde_cloudflare = 0) { return 403; }`. Se usa `geo` sobre `$realip_remote_addr` porque `allow/deny` evaluaría la IP ya reescrita por realip (bloquearía a todos).
- Puerto 80 también restringido; el reto HTTP-01 de Let's Encrypt llega por Cloudflare (`certbot renew --dry-run` OK).
- `cf-actualizar-ips` (`/usr/local/sbin`) + `cf-actualizar-ips.timer` (semanal, lunes): descarga ips-v4/ips-v6, valida CIDR, regenera geo+realip, `nginx -t` y recarga solo si pasa (si falla, restaura).
- abp-template sigue en inangelroque.duckdns.org (fuera de Cloudflare).
- fail2ban: los logs muestran la IP real, pero un ban por iptables no frena a un visitante que entra por Cloudflare (la conexión TCP viene de IPs de Cloudflare). Para eso habría que usar reglas/WAF de Cloudflare.

Rollback: quitar las dos líneas `include .../cloudflare-*.conf` de los sitios, `nginx -t && systemctl reload nginx`; `systemctl disable --now cf-actualizar-ips.timer`.
