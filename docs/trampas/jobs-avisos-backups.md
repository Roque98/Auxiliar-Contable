# Trabajos en segundo plano, avisos (campana) y copias de seguridad

> Parte de `CLAUDE.md` — extraído para no cargar todo el historial de trampas en cada sesión. Ver el índice en la raíz.

## 6.0 Trabajos en segundo plano (background jobs)

Sistema de trabajos de ABP con el proveedor de base de datos (tabla `AbpBackgroundJobs`).
Los **argumentos** viven en `Application.Contracts/BackgroundJobs/JobArgs.cs` (son un
contrato, y así se pueden testear); las **implementaciones** en `HttpApi.Host/BackgroundJobs/`,
que es donde están el cliente HTTP y el token del bot.

- ⚠️ **TRAMPA — un job NO viene envuelto en una unidad de trabajo.** A diferencia de un
  application service, aquí no hay UoW ambiental: la primera consulta revienta con
  *«A DbContext can only be created inside a unit of work!»*. Hay que abrirla a mano,
  igual que hacen los jobs del propio ABP:
  `using var unidad = _unidadesDeTrabajo.Begin(requiresNew: true); … await unidad.CompleteAsync();`
  No compila mal ni falla en los tests: solo revienta al ejecutarse de verdad.
- ⚠️ **Un job tampoco hereda el tenant** de la petición que lo encoló. Si el trabajo toca
  datos de un tenant, el `TenantId` va en los argumentos y se restaura con
  `ICurrentTenant.Change(...)`.
- ⚠️ **Los argumentos se guardan como JSON en la base. NUNCA metas un secreto ahí.** El
  enlace de recuperación de contraseña encola solo el `UserId`; el token se genera *dentro*
  del job, justo antes de enviarlo. Si viajara en los argumentos, quedaría legible para
  cualquiera con acceso a la base — y, si el job fallara, hasta dos días. Hay un test que
  lo comprueba sobre el JSON tal cual queda guardado.
- **Fija el nombre con `[BackgroundJobName("template:...")]`.** Por defecto ABP usa el
  `FullName` del tipo de argumentos y lo guarda en la fila: renombrar o mover la clase
  dejaría huérfanos los trabajos encolados en ese momento.
- **Lanzar una excepción es la forma de pedir un reintento** (espera creciente: 1 min, 2,
  4…, abandonado a los 2 días). Atraparla dentro del job devuelve el problema al punto de
  partida: un fallo silencioso.
- En los tests de integración la ejecución está **desactivada**
  (`AbpBackgroundJobOptions.IsJobExecutionEnabled = false`): las implementaciones viven en
  el Host, que esos tests no cargan, y así la fila se puede inspeccionar tal cual queda.
- `IBackgroundJobManager` resuelve a `NullBackgroundJobManager` —que **lanza** al encolar—
  si el sistema no está montado. Hay un test que lo vigila.

### 6.0.1 El panel de la cola (`/jobs`)

Ver qué está encolado, qué reintenta, qué se abandonó y **por qué**. Permisos
`AuxiliarContable.Jobs` (ver) y `AuxiliarContable.Jobs.Manage` (reintentar y borrar).

- ⚠️ **ABP NO GUARDA EL MOTIVO DEL FALLO.** `BackgroundJobRecord` tiene `TryCount`,
  `NextTryTime` e `IsAbandoned` y **ni un solo campo para la excepción** (verificado en
  `refs/abp`). Sin eso, el panel diría «falló cinco veces» y habría que ir al log del
  servidor igualmente. De ahí una tabla propia, `AppJobFailures`, con **una fila por
  intento** — no una por trabajo: fallar siempre por lo mismo y fallar por algo distinto
  cada vez son problemas diferentes, y con solo el último error se ven igual.
  - La llena `WorkerQueGuardaLosFallos`, que **envuelve** `TryExecuteJobAsync` llamando a
    `base` en vez de copiar su cuerpo. Copiarlo ataría el reintento con espera creciente y
    el abandono a los dos días a una versión concreta de ABP, y esas reglas seguirían
    pareciendo suyas mientras dejaban de serlo.
- ⚠️⚠️ **`AbpBackgroundJobWorkerOptions.ApplicationName` vale `null` por defecto**, y lo
  usan los DOS lados: `DefaultBackgroundJobManager` lo escribe al encolar y el worker
  filtra por él (`IsJobEligible`). Consecuencia al sembrar trabajos a mano en la base: una
  fila **con** nombre de aplicación es **invisible para el worker** y se queda ahí para
  siempre, sin error ni log. `ApplicationName` va **NULL**.
- ⚠️ **Un trabajo que termina bien se BORRA por defecto** (`StoreSuccessfulJobs = false`).
  Con eso el panel solo enseñaría lo roto, y entonces «no hay nada» no se distingue de «el
  worker no está corriendo» — que es justo la pregunta que uno se hace al abrirlo. El Host
  lo pone en `true` con retención corta (`JobConsts.SuccessfulJobRetentionDays`). ABP
  registra su `BackgroundJobCleanupWorker` **solo si** están puestas esa opción **y**
  `SuccessfulJobRetentionTime`: sin las dos, nadie borraría los completados.
- ⚠️ **`AsyncLocal` NO sirve para sacar la excepción del ejecutor.** Un valor asignado
  dentro de un método llamado **no vuelve al llamador**, y la escritura va justo en ese
  sentido: el ejecutor (que tiene la excepción) está más adentro que el worker (que tiene
  la fila). Se usa un buzón **scoped**, `IUltimoFalloDeJob`; el worker abre un ámbito por
  ciclo —o por trabajo, si algún día se ejecutan en paralelo— y ambas piezas resuelven la
  misma instancia.
  - Y el worker **no puede inyectar nada con ámbito por constructor**:
    `BackgroundJobWorkerManager` lo resuelve del contenedor **raíz**. Lo que necesite se
    saca de `workerContext.ServiceProvider` dentro del método. (Sustituir el worker sí
    funciona: el manager lo resuelve de DI, y ABP lo documenta.)
  - Un fallo **previo** a ejecutar el trabajo —nombre desconocido, argumentos que no
    deserializan— no pasa por el ejecutor: llega a `HandleJobErrorAsync`, que **abandona al
    primer intento, sin reintentos**. Ese camino usa un diccionario por id de trabajo. Es
    exactamente el escenario de «renombraron la clase de argumentos», y ahora se ve.
- ⚠️ **El estado no es una columna**: se deriva de `CompletionTime`, `IsAbandoned` y
  `TryCount`, y está escrito **dos veces** — como `WHERE` de SQL en el repositorio y en C#
  para la etiqueta de la fila. Tiene que filtrar en la base: si se filtrara en memoria, el
  `Take` recortaría antes y pedir «abandonados» devolvería cero con la cola llena de ellos.
  Hay un test que compara las dos formas; si se separan, el panel etiqueta las filas de una
  manera y las filtra de otra. **Ojo al orden**: se mira primero `CompletionTime`, porque un
  trabajo completado tras un reintento también tiene `TryCount > 0`.
- `IBackgroundJobRepository` de ABP solo ofrece `GetWaitingListAsync` (pendientes y
  ejecutables **ya**): lo abandonado, lo que espera un reintento futuro y lo completado
  quedan fuera. De ahí un repositorio propio para leer, y el de ABP para tocar un trabajo
  por id.
- **Reintentar** es solo `IsAbandoned = false` + `NextTryTime = ahora`; lo recoge el worker
  en su siguiente pasada. **`TryCount` no se pone a cero**: es la señal de que esto lleva
  días fallando. Efecto de dejarlo: ABP abandona cuando el próximo intento caería a más de
  `DefaultTimeout` (2 días) de la **creación**, así que un trabajo viejo recibe **un
  empujón, no un ciclo nuevo**. Es lo que se quiere.
- **Reintentar un completado se rechaza**: volvería a ejecutarlo. Un aviso duplicado es
  ruido; un correo o un cobro duplicado, no — y desde el panel no se distinguen.
- **Borrar el trabajo NO borra su historial de fallos**: sobrevive a propósito, para poder
  contestar «esto ya nos pasó el mes pasado». Lo limpia `JobFailuresPurgeWorker`.
- El panel **muestra los argumentos enteros**, y se puede precisamente porque §6.0 prohíbe
  meter secretos ahí (hay un test que lo vigila sobre el JSON guardado). Esta pantalla es la
  razón práctica de aquella regla: cualquiera con el permiso los lee.

## 6.02 Avisos dentro de la aplicación (la campana)

ABP open source **no trae sistema de notificaciones** (es de Pro). Hasta ahora todo aviso
salía solo por Telegram, que únicamente tiene quien haya vinculado su cuenta: si nadie la
vinculó, el aviso no existía para nadie. Campana en la barra superior + pantalla `/notifications`.

- **Un aviso apunta a un PERMISO, no a una lista de personas**, y se guarda **una sola fila**.
  Repartirlo obligaría a enumerar en el momento del envío a todos los usuarios con ese
  permiso —en ABP, cruzar concesiones por rol y directas— y dejaría fuera a quien se nombre
  administrador mañana, que es justo quien necesita ver lo que pasó. También admite
  destinatario personal (`UserId`); el constructor exige **exactamente uno de los dos**,
  porque las dos formas de equivocarse producen un aviso que parece enviado y no lo está.
- ⚠️ **«Leído» NO puede ser una columna del aviso**: un aviso por permiso lo ven varias
  personas y cada una lo lee cuando le toca. Con una columna, el número rojo desaparecería
  para todos en cuanto lo abriera el primero. Vive en `AppNotificationReads`, una fila por
  (aviso, persona) creada **al leer**: «no leído» es la **ausencia** de fila.
- ⚠️ **El filtro de permisos se aplica SIEMPRE, también con la lista vacía.** Escrito como un
  filtro opcional al uso («si no hay permisos, no filtres»), cualquier usuario recién creado
  vería de golpe todos los avisos de administración de la instalación. Hay un test dedicado.
- **Todo aviso operativo pasa por `IAvisador`**, que escribe en la campana y encola Telegram
  en **una** llamada. Antes cada productor encolaba Telegram por su cuenta; con dos canales,
  el día que uno se olvidara del segundo el aviso existiría en un sitio y no en el otro sin
  que nada fallara. El orden importa: primero la campana (escritura local) y luego el
  encolado, y **ningún canal puede tumbar a quien avisa** — un recolector que muere porque no
  pudo avisar deja de recolectar.
- El **vigilante de trabajos abandonados** (`AbandonedJobsWatchWorker`) avisa **por tanda, no
  por trabajo**: un fallo sistémico los abandona todos a la vez y un aviso por cada uno sería
  una avalancha justo cuando hay que leerlo. Y **no repite**: antes de avisar comprueba si ya
  hay un aviso con el mismo título en las últimas 24 h. Se compara por **título** —que
  incluye el recuento— porque es lo que ve la persona: si el número cambia, es otro problema.
- ⚠️ **`AbpAsyncTimer.RunOnStart` es `false` por defecto** (verificado en `refs/abp`): un
  worker periódico no corre hasta cumplirse el primer período. Con período de una hora y
  despliegues frecuentes, el vigilante podría no llegar a correr nunca. Lleva
  `Timer.RunOnStart = true`; recién arrancado es justo cuando interesa saber qué quedó a
  medias, y el freno de 24 h impide que los reinicios repitan el aviso.

### 6.02.1 Angular: el color de fondo correcto en LeptonX

- ⚠️⚠️ **`--bs-body-bg` NO es la superficie de contenido.** En LeptonX Lite vale `#161616`
  —el fondo **oscuro del armazón**— mientras que `--bs-border-color` y `--bs-tertiary-bg` son
  claros. Usarla para un panel flotante o un bloque de código lo pinta **negro con bordes
  claros**, y el texto hereda además el gris apagado de la barra. La variable de la
  superficie es **`--lpx-card-bg`** (`#ffffff`). Pasó dos veces: el panel de la campana y el
  bloque «Argumentos» de `/jobs` —este último parecía un bloque de código a propósito—.
  - Se encontró **midiendo `getComputedStyle` en el navegador**, no leyendo CSS: el valor
    depende del tema activo y no está escrito en ningún sitio del repo.
- **La campana se engancha con `NavItemsService.addItems([new NavItem({ component })])`**, el
  punto de extensión de la barra que usan el selector de idioma y el menú de usuario. No hace
  falta sustituir la barra entera (que dejaría de actualizarse con el módulo).
- ⚠️ Un **botón de icono pequeño con un badge encima**: con `padding` escaso el número tapa
  el icono por completo y desde fuera parece que no se pinta. Comprobado midiendo el rect
  (25×27 px), no mirando.
- **El contador vive en el servicio**, no en el componente: lo cambian **dos** sitios (la
  campana, que sondea, y la pantalla, donde se marca leído). Con un contador por componente,
  marcar todo como leído dejaría la campana con el número viejo hasta el siguiente sondeo.

### 6.02.2 Caducidad de claves de API

`ApiKeyExpiryWatchWorker` avisa al dueño con `ApiKeyConsts.AvisoCaducidadDias` (7) de
antelación. Antes una clave caducaba en silencio y lo que la usaba empezaba a recibir 401
sin nada que relacionara una cosa con la otra.

- **Es un aviso PERSONAL** (`UserId`), no dirigido a un permiso: una clave es de quien la
  creó, y avisar a todos los administradores sería ruido *y* contarles qué integraciones
  tiene montadas cada uno. Hay un test que lo comprueba con un usuario que tiene todos los
  permisos y aun así no lo ve.
- **No se manda por Telegram**, al revés que los avisos operativos: no es una incidencia de
  la instalación y el canal de Telegram va a los chats de monitoreo, no a la persona. Por eso
  escribe en el repositorio directamente y no pasa por `IAvisador`.
- ⚠️ **El freno anti-repetición compara título Y usuario.** El título lleva el nombre de la
  clave, y «producción» es el nombre más probable del mundo: comparando solo por título, la
  primera persona recibiría su aviso y la segunda se quedaría sin el suyo sin que nada
  fallara. Hay un test.
- **Las ya caducadas y las revocadas quedan fuera**: avisar de algo que ya pasó no evita nada
  y llenaría la campana en cada pasada, para siempre, porque la fila se conserva.
- ⚠️ La consulta usa **`IgnoreQueryFilters()`**, igual que la búsqueda por hash: el worker
  corre sin tenant, y sin eso solo vería las claves del host — las de los tenants caducarían
  siempre en silencio, que es justo lo que se viene a evitar. El aviso se dirige después al
  dueño y al tenant que guarda la propia clave.

## 6.03 Copias de seguridad vigiladas y panel de estado

Las copias las hace **systemd, fuera de la aplicación** (03:30 diaria; verificación por
restauración el día 1 de cada mes). Ver `deploy/README.md` para el destino remoto.

- ⚠️ **El puente es un archivo, no una llamada a la API**: los scripts dejan
  `/var/lib/auxiliar-contable/backup-status.json` y `restore-check-status.json`, y la aplicación
  los lee. Se eligió así porque la copia tiene que poder correr —y dejar constancia— aunque
  la aplicación esté caída, que es justo cuando más importa. Se escribe con un `mv` atómico
  y con un `trap … EXIT`, o sea **también cuando el script falla**: el caso a detectar es
  «lleva semanas rota», y para eso el archivo tiene que contar el fallo, no dejar de
  actualizarse.
  - La unidad lleva `ProtectSystem=strict`, así que `/var/lib/auxiliar-contable` va declarado en
    `ReadWritePaths` — sin eso el script muere con *Read-only file system*.
- ⚠️ **`BackupHealthCheck` NUNCA devuelve `Unhealthy`, y es deliberado.** El despliegue
  comprueba `/health-status` y aborta si no da 200; ASP.NET devuelve **503 para
  `Unhealthy`** y 200 para `Degraded`. Una copia fallida marcada como `Unhealthy`
  **bloquearía el propio despliegue que traería el arreglo**, y haría parecer caída una
  aplicación que sirve perfectamente. La urgencia la llevan los avisos, no el semáforo.
- **El criterio de aviso vive en el dominio** (`BackupAlertRules.Evaluar`), no en el worker:
  el worker solo lee archivos y escribe avisos. Separado se prueba sin disco, sin base y sin
  systemd, y tiene más tests que código. Informa de **lo más grave y para** — un aviso por
  cada cosa rota sería una avalancha justo cuando hace falta claridad.
- ⚠️ **El vigilante NO se registra en Development.** Ahí no hay copias por diseño, así que
  avisaría de su ausencia en cada arranque. Un aviso permanente que nadie puede resolver
  enseña a ignorar la campana, y entonces el aviso del día que importe tampoco se lee.
- «Verificación por restauración» es lo único que demuestra que la copia **sirve**, no solo
  que se subió: baja el snapshot desde R2, lo restaura en una base temporal y exige señal de
  vida (≥20 tablas y ≥1 usuario). Una base restaurada vacía es el fallo más traicionero,
  porque `pg_restore` termina con éxito sobre un volcado sin datos.
  - Su unidad lleva `PrivateTmp=false` a propósito: el volcado se copia a `/tmp` para que el
    usuario `postgres` pueda leerlo, y con `/tmp` privado postgres vería otro distinto.
- **El panel `/system-status`** (permiso `AuxiliarContable.SystemStatus`) enseña **solo recuentos**.
  Eso es lo que permite que baste un permiso: saber que hay tres trabajos abandonados no es
  lo mismo que poder leer sus argumentos, y para eso sigue haciendo falta `AuxiliarContable.Jobs`.
  La salud sale de la última muestra del recolector, no de sondear otra vez.

