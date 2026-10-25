# Archivos (BLOBs), claves de API, bloqueo de IPs, export CSV y sesiones activas

> Parte de `CLAUDE.md` — extraído para no cargar todo el historial de trampas en cada sesión. Ver el índice en la raíz.

## 6.1 Archivos (BLOBs) y foto de perfil

Los archivos se guardan con el sistema de **BLOB Storing de ABP**, proveedor
**Database** (tablas `AbpBlobs` / `AbpBlobContainers`, ya en la migración `Initial`).

- El proveedor va **declarado explícitamente** en `IgasConsoleDomainModule`
  (`options.Containers.ConfigureDefault(c => c.UseDatabase())`) aunque
  `BlobStoringDatabaseDomainModule` lo elija solo: ese automatismo solo actúa *si nadie
  ha configurado un proveedor*, así que el día que se añada otro (S3, disco…) dejaría de
  aplicarse **en silencio**.
- Sin Docker y con un solo servidor, la base de datos es el destino correcto: no hay
  volumen compartido que montar y los archivos entran en la copia de seguridad de la base.
  Con más de un servidor de aplicación esto es lo único que habría que cambiar.
- **Contenedor tipado** por cada tipo de archivo:
  `[BlobContainerName("...")] class XContainer {}` → inyectar `IBlobContainer<XContainer>`.
  El nombre va en una **constante**, no deducido del nombre de la clase: renombrarla dejaría
  los archivos ya guardados bajo el nombre viejo. Ver `Domain/Files/ProfilePictureContainer.cs`.

**Feature de referencia para archivos: la foto de perfil** (`Files/`). ABP open source
**no** trae foto de perfil (es de Account Pro), así que está hecha a mano:

- `ProfilePictureAppService` — subir/consultar/borrar la del usuario actual. Sin permiso
  propio (cada uno gestiona la suya); el id sale **siempre de la sesión**, nunca de la petición.
- Rutas que genera ABP (verificadas en Swagger): `GET`/`DELETE /api/app/profile-picture`,
  `POST /api/app/profile-picture/upload`. El campo del formulario **tiene que llamarse
  `file`**, como el parámetro: así lo empareja `AbpRemoteStreamContentModelBinder`.
- ⚠️ **El formato se detecta por los BYTES, no por el `Content-Type`** (`DetectorDeImagen`).
  Ese header lo elige quien sube y puede mentir: un HTML con `<script>` declarado como
  `image/png` sería **XSS almacenado** al devolverlo. **SVG queda fuera a propósito** aunque
  sea una imagen: admite scripts y ninguna firma de bytes distingue uno inofensivo.
- ⚠️ **El tamaño se comprueba DOS veces.** `ContentLength` es solo un atajo para cortar antes
  de leer nada; lo declara quien sube y **puede faltar o mentir** (por ejemplo con
  `Transfer-Encoding: chunked`). La defensa real es el tope **mientras se lee**. Hay un test
  con un stream no buscable que prueba justo ese camino.
- El tipo **no se guarda** junto a la foto: se vuelve a deducir de los bytes al leer. Así no
  hace falta una tabla extra para un dato que la imagen ya lleva dentro, ni puede quedar
  desincronizado.
- La respuesta es un **`data:` URL dentro del JSON**, no binario aparte: el destino es un
  `<img src>` y el avatar de la barra superior, que esperan una cadena. Devolver bytes
  obligaría a una segunda petición autenticada — o endpoint anónimo, o token en la URL —
  para una imagen que reescalada pesa decenas de KB.
- **El reescalado ocurre en el navegador** (`normalizar-imagen.ts`, canvas → JPEG 512px).
  Es comodidad, no seguridad: quien llame a la API directamente se salta esa pantalla, y
  por eso el límite de bytes del servidor es el que cuenta. Usa
  `createImageBitmap(..., { imageOrientation: 'from-image' })`: sin eso, las fotos de móvil
  en vertical se suben tumbadas (la rotación va en los metadatos EXIF, que el canvas ignora).
- **Avatar de la barra superior**: LeptonX fija el avatar a un icono porque ABP open source
  no tiene foto. Se parchea con `UserProfileService.patchUser({ avatar })` desde
  `profile-picture/avatar.provider.ts`. ⚠️ `patchUser` hace emitir a `user$`, que vuelve a
  entrar en el mismo suscriptor: **la guarda que compara la fuente actual antes de parchear
  es lo único que evita un bucle infinito**.

## 6.05 Claves de API

Acceso máquina a máquina sin login interactivo: cabecera `X-Api-Key`. Pantalla en
`/api-keys`, permiso `IgasConsole.ApiKeys`.

- **Una clave autentica COMO un usuario y hereda sus permisos.** Se descartó darle permisos
  propios: un segundo sistema de autorización en paralelo acaba divergiendo del primero
  —se concede algo a un rol y nadie se acuerda de las claves—. Así, revisar qué puede hacer
  una clave es revisar qué puede hacer su dueño.
- El principal se construye con **`IUserClaimsPrincipalFactory`**, la misma fábrica que el
  login. Rehacer los claims a mano sería una segunda definición de «quién eres» condenada a
  separarse de la primera.
- ⚠️ **Solo se guarda el hash**, y la clave se devuelve **una única vez**. Se usa **SHA-256,
  no bcrypt/PBKDF2**: un hash lento protege secretos de baja entropía (contraseñas que se
  adivinan probando); esto son 256 bits aleatorios, y ralentizarlo solo castigaría cada
  petición legítima. Lo que sí hace falta es que sea determinista, porque se busca POR él.
- La clave lleva el **prefijo `tpl_`**: si aparece en un log o un repositorio, se sabe al
  instante qué se ha filtrado. Una cadena aleatoria sin prefijo no dice nada.
- **Sin caché, al revés que el bloqueo de IPs.** Allí cada petición pagaba la consulta; aquí
  solo las que traen clave. A cambio, **revocar surte efecto al instante**, que en una
  credencial vale más que unos microsegundos.
- ⚠️ `FindByHashAsync` usa **`IgnoreQueryFilters()`**: la clave llega antes de que haya
  tenant resuelto —es justo lo que se está averiguando—, así que el filtro de multi-tenancy
  escondería la fila y ninguna clave de un tenant funcionaría nunca.
- El middleware va **después de `UseAuthentication`** (sobrescribe `context.User`, así que
  la clave manda sobre cualquier cookie que venga también) y **antes de `UseAuthorization`**.
  El 401 escribe cuerpo y `ContentType`, por la trampa de `UseErrorPage`.
- `LastUsedUtc` se actualiza **como mucho cada 5 minutos**: escribirla en cada petición
  convertiría toda llamada de lectura en una escritura. Es el dato que se mira antes de
  revocar una clave de la que ya nadie se acuerda.

## 6.15 Seguridad: bloqueo de IPs

**Módulo aparte de Monitoring** (`Security/` en cada capa, namespace `IgasConsole.Security`,
pantalla propia en `/security`, permisos `IgasConsole.Security.BlockedIps` y `.Manage`). Mirar
un gráfico de CPU y dejar a alguien fuera de la aplicación no se parecen en nada, y bajo un
permiso común no se podía dar lo uno sin lo otro.

Los dos módulos se tocan en **un solo punto**: `AbusiveIpSource` (capa de infraestructura),
que traduce los rollups del rate limiter en candidatas. Los dominios no se conocen.

### Bloqueo manual

- **El middleware decide contra una copia EN MEMORIA**, nunca contra la base: se comprueba en
  cada petición y una consulta por petición penalizaría a todo el tráfico legítimo para
  atrapar a unos pocos. `BlockedIpCache` es singleton y **reemplaza el diccionario entero**
  en cada refresco, así que el camino caliente no necesita lock.
- Se refresca **al bloquear/desbloquear** (inmediato) y **cada 30 s** por un worker — este
  último para los bloqueos que caducan solos y para la primera carga tras un despliegue.
- ⚠️ Posición en el pipeline: **después de `UseForwardedHeaders`** (si no, detrás de nginx
  todo parece venir de 127.0.0.1) y **después de `RequestMetricsMiddleware`** (para que el
  tráfico cortado siga contando en las métricas). Antes de todo lo demás.
- ⚠️ **El 403 escribe cuerpo y `ContentType`**, por la misma razón que el 429 del rate
  limiting: `UseErrorPage()` convierte fuera de Development cualquier 4xx **sin cuerpo** en
  un 302.
- ⚠️ **No se puede bloquear la propia IP**: el panel se administra desde la misma web que
  protege. Hay un test.
- Un bloqueo caducado **deja de aplicar pero sigue en el panel**: saber que algo estuvo
  bloqueado es información. El motivo es obligatorio.

### Bloqueo automático

`AutoBlockWorker`. Señal: rechazos acumulados por IP en una ventana.
Configuración en `appsettings:Security:AutoBlock`.

⚠️ **Las dos señales corren a ritmos distintos** (`SecurityConsts`): la de volumen cada
`AutoBlockEvaluationSeconds` (60 s), porque su fuente `RateLimitEvent` es un rollup por
minuto y releerlo antes no ve nada nuevo; la de sondeo cada `AutoBlockScanEvaluationSeconds`
(20 s), porque `AbpAuditLogs` tiene una fila por petición y esperar un minuto entero solo
alarga lo que el escáner alcanza a probar antes del corte. El worker late al ritmo rápido y
la señal de volumen se autolimita dentro con `_ultimoVolumenUtc`. Coste: un `GROUP BY` sobre
`AbpAuditLogs` 3× más frecuente, acotado porque filtra primero por `ExecutionTime`, indexado.

- **DOS señales, y hacen falta las dos:**
  1. **Rechazos del rate limiter** → detecta *volumen* (avalanchas).
  2. **Sondeo de rutas** (`GetScannersAsync`, sobre `AbpAuditLogs`) → detecta
     *comportamiento*: muchas peticiones fallidas y casi todas a rutas distintas.
- ⚠️⚠️ **La señal 1, SOLA, no bloqueaba a nadie**, y así estuvo en producción. Medido con
  tráfico real: los escaneadores iban a **378 y 591 peticiones por minuto con la cuota en
  600**, así que nunca llegaban a ser rechazados — y por tanto nunca se contaban. En toda
  la vida de la instalación había 69 rechazos frente a un umbral de 300. Probaban
  `/.env`, `/credentials/aws.env`, `/.ssh/id_ecdsa.pub`… buscando credenciales.
- **El umbral sale de los datos, no de la intuición**: 20 fallos con ≥80 % de error en 10
  min. Los escaneadores medidos fallaban el 92-100 % y hacían entre 83 y 2490 peticiones
  fallidas; el cliente legítimo más activo, el 0-2 % y 3 fallos. El recuento se deja bajo a
  propósito —sigue siendo 4× el peor caso legítimo conocido— porque **la condición que de
  verdad separa un escáner de un cliente con enlaces rotos es el porcentaje**, no el
  recuento; cuanto antes se alcanza, menos alcanza a probar el escáner. Y las ráfagas son
  concentradas —1750 fallos en un solo cubo de 10 minutos—, así que la ventana corta es la
  correcta.
- **Las dos condiciones van juntas.** Solo con el recuento se vetaría a un cliente muy
  activo que acumula 404 sueltos; solo con el porcentaje, a quien entró una vez a una URL
  mala. Hay un test por cada uno de esos dos falsos positivos.
- ⚠️ **El aviso va DENTRO de la unidad de trabajo, antes de cerrarla.** Estaba después, y
  por eso **nunca llegaba a la campana**: escribirlo necesita un `DbContext` y reventaba
  con *«A DbContext can only be created inside a unit of work!»*. `Avisador` se traga esa
  excepción a propósito —un canal caído no puede tumbar al worker que bloquea—, así que el
  fallo no salía por ningún sitio: las IPs se bloqueaban bien y el aviso desaparecía en
  silencio. Solo se vio ejecutándolo y mirando el log.
- **Siempre con caducidad.** Un veto automático indefinido convierte un umbral mal puesto en
  un problema permanente que nadie recuerda haber creado.
- **`NeverBlock` con el loopback de serie**: por ahí entran los health checks, y bloquearlo
  dejaría la aplicación marcada como caída por sí misma.
- **`MaxBlocksPerRun`** es un cortafuegos contra el propio mecanismo: ante un ataque
  distribuido, sin tope una pasada llenaría la tabla y el panel dejaría de ser legible justo
  cuando más falta hace.
- **No pisa decisiones humanas**: si la IP ya tiene una fila, la respeta.
- `IsAutomatic` distingue el origen en el panel, y cada tanda **encola un aviso** — que la
  aplicación vete a alguien sola es justo lo que hay que saber sin mirar un panel.
- ⚠️ **EF Core no traduce la construcción de un record posicional dentro de un `GroupBy`**
  («The LINQ expression could not be translated»). Se proyecta a un **tipo anónimo** y el
  record se construye después, sobre las pocas filas ya devueltas. Ver `AbusiveIpSource`.

### 6.15.1 Registro de seguridad (`/security`)

`AbpSecurityLogs` la llena ABP **sola desde el primer día** —inicios de sesión correctos y
fallidos, bloqueos, cambios de contraseña, segundo factor— y hasta ahora no había forma de
verla. Segunda sección de `/security`, permiso propio `IgasConsole.Security.Logs`: ver quién
intentó entrar es lo más sensible del panel.

- Aquí **sí basta el repositorio de ABP**: `IIdentitySecurityLogRepository` trae
  `GetListAsync` con todos los filtros y `GetCountAsync`. Al revés que con los trabajos en
  segundo plano, donde hubo que escribir uno propio.
- El filtro es por **acción exacta**, no un «solo fallidos» que agrupe varias. Agrupar
  obligaría a filtrar en memoria DESPUÉS de paginar, y entonces el total seguiría siendo el
  de todas las entradas: la pantalla diría «3 de 40» y ese 40 no significaría nada.
- Las acciones del desplegable **se calculan de los datos**, no de una lista fija: los
  nombres los pone el módulo Identity y pueden cambiar al actualizarlo. Una lista fija se
  quedaría desfasada en silencio y ofrecería filtros que no encuentran nada.
- Cada fila enlaza con **bloquear esa IP**, que es la acción natural al ver una racha de
  intentos fallidos desde la misma dirección.

- ⚠️⚠️ **TRAMPA — un DTO de entrada NO puede tener una propiedad llamada `Action`.** En MVC,
  `action` es un valor de **ruta** (el nombre del método), y el enlazador de modelos rellena
  con él cualquier propiedad que se llame así **aunque nadie la mande en la petición**. El
  DTO llegaba con `Action = "GetList"` y la consulta filtraba por eso: **cero filas, ningún
  error, con la tabla llena**. Lo mismo vale para `controller`, `area`, `page` y `handler`.
  - **Los tests de integración NO lo cazan**: llaman al application service directamente y
    ahí no hay enlazador de modelos. Los siete pasaban con el fallo puesto.
  - Se encontró **mirando el SQL generado** de una petición HTTP real
    (`Microsoft.EntityFrameworkCore.Database.Command` a `Information` en
    `appsettings.Development.json`). El `WHERE` llevaba un filtro que nadie había pedido.
  - Lo vigila el test de arquitectura
    `Ningun_DTO_de_entrada_usa_un_nombre_reservado_por_el_enrutado_de_MVC`, que detecta los
    DTO de entrada **por uso** (parámetros de métodos de application services) y no por el
    sufijo del nombre. Comprobado que se pone rojo al renombrar la propiedad.

### 6.15.2 Historial de cambios de entidades (`/metrics`)

Qué campo cambió y de qué valor a cuál. Las tablas (`AbpEntityChanges` /
`AbpEntityPropertyChanges`) estaban desde la migración inicial sin nadie que las llenara:
lo que faltaba era **declarar qué entidades rastrear**. Mismo permiso que el registro de
peticiones (`IgasConsole.AuditLogs`) — las dos cosas son el rastro de personas.

- ⚠️ **La lista de entidades es EXPLÍCITA, nunca `AddAllEntities()`.** Seleccionar todo
  sería catastrófico aquí: `ServerMetric` inserta **una fila por minuto**, y
  `RateLimitEvent`, `Notificacion` y `JobFailure` escriben continuamente. Cada inserción
  generaría una fila de cambio más una por columna — las tablas de auditoría crecerían más
  rápido que los datos que auditan. Se rastrea `Product`, `BlockedIp`, `ApiKey`,
  `IdentityUser` e `IdentityRole`.
- **El usuario NO está en `EntityChange`**: vive en el `AuditLog` padre. Se cruza en la
  consulta; hacerlo después sería N+1 para pintar una tabla. Las propiedades van en una
  **segunda** consulta y no como `Include`, que multiplicaría cada fila por sus columnas.
- ⚠️ **ABP guarda los valores serializados en JSON**: un texto llega como `"Teclado"` —con
  comillas— y un nulo como la cadena `"null"`. Se limpia en el application service, no en
  la pantalla: es una peculiaridad del almacenamiento, y cualquier cliente de la API se
  encontraría lo mismo.
- El cambio solo se registra si hay un **registro de auditoría** que lo envuelva, o sea
  dentro de una petición. Lo que hace un worker (por ejemplo el bloqueo automático de IPs)
  **no aparece**, y por eso la columna «Quién» puede venir vacía.

### 6.15.3 Límites por identidad (usuario y clave de API)

El rate limiting por IP no cubre a los propios: un script con un bucle roto sale de una
dirección legítima —a menudo la misma que usan compañeros— y bajar el límite de IP para
atraparlo dejaría fuera a los demás.

- ⚠️⚠️ **NO se puede hacer con `UseRateLimiter()`.** Ese middleware va en el pipeline
  **antes** de `UseAuthentication()`, y tiene que seguir ahí: su trabajo es cortar
  avalanchas anónimas antes de que lleguen a validar tokens o a buscar claves en la base.
  En ese punto `context.User` está **vacío** y no hay identidad que limitar. Por eso hay un
  middleware propio (`IdentityRateLimitMiddleware`) **después** de `ApiKeyMiddleware`.
  - Los dos conviven y la petición pasa por ambos: el de IP protege de extraños, este de
    los propios.
- ⚠️ **Hizo falta un claim nuevo** (`ApiKeyConsts.ClaimTipo`). El principal de una clave de
  API se construye con la MISMA fábrica que el login —eso es deliberado, para que los
  permisos no diverjan— y por tanto sale **indistinguible de una sesión de navegador**. Sin
  esa marca, todas las claves de una persona compartirían su cuota y un script desbocado
  dejaría sin servicio a los demás scripts de su dueño y a su propia sesión.
- **Cuotas**: clave de API < usuario, y usuario > IP. Un usuario autenticado necesita MÁS
  que una IP anónima (una pantalla con varios paneles hace muchas llamadas); una máquina
  necesita menos que una persona. Hay tests que vigilan esas relaciones — romperlas no falla
  en ningún sitio, simplemente empieza a cortar a quien no debía.
- El 429 **escribe cuerpo y `ContentType`**, por la trampa de `UseErrorPage()` (§5).
- El estado vive en memoria y se **purga por inactividad**: sin eso el diccionario crecería
  con cada identidad vista y no se vaciaría nunca.
- Verificado contra la API real, que es la única forma: 5 pasan y el resto 429 con la cuota
  de IP a 5000 (o sea, cortó la identidad), y **una segunda clave del mismo usuario y la
  misma IP conserva su propia cuota**.

## 6.2 Exportar a CSV

`EscritorCsv` (capa Application) + endpoints `…/recent-csv` y `…/blocked-events-csv`.
Tres decisiones que casi ninguna librería toma por ti:

- ⚠️ **BOM de UTF-8 obligatorio.** Excel abre los `.csv` en la codificación regional
  (Windows-1252), no en UTF-8, salvo que el archivo empiece por el BOM. Sin él «Ratón» se ve
  «RatÃ³n» y parece un problema de la base de datos cuando es del lector. Ojo: `GetBytes` NO
  añade el preámbulo por su cuenta (solo lo hace un `StreamWriter`).
- ⚠️ **Separador `;`, no `,`.** Excel usa el separador de la configuración regional; en
  español es el punto y coma, y con comas toda la fila cae en la primera columna. El precio:
  quien lea con pandas necesita `sep=';'`. Es una constante en `EscritorCsv`.
- ⚠️ **Inyección de fórmulas (CSV injection).** Excel y Sheets **ejecutan** las celdas que
  empiezan por `= + - @`. Estos exports llevan datos que escribe cualquiera desde fuera
  (URLs, nombres de usuario), así que esas celdas se prefijan con un apóstrofo. Escapar
  comillas arregla el formato, no esto.
- **El verbo importa**: un método `Export…Async` lo mapea ABP a **POST**; `Get…CsvAsync` a
  **GET**, que es lo correcto para una descarga. Confirmar siempre en Swagger.
- En Angular la descarga NO puede ser un `<a href>`: el endpoint va autenticado y una
  navegación no lleva la cabecera `Authorization`. Se pide como blob con `HttpClient` y se
  guarda en memoria (ver `metrics/descargar.ts`). Meter el token en la URL lo dejaría en el
  historial y en los logs.
- `responseType` va en la **config** de `RestService`, no en la request (solo mira el de la
  config), y como literal `'blob' as any`: `Rest.ResponseType` es un `const enum` de un
  `.d.ts` que Angular no deja importar con `isolatedModules`.

## 6.7 Sesiones activas

Ver dónde tienes la sesión abierta y cerrarla en remoto (`/sessions`, sin permiso propio:
cada uno ve las suyas). Complementa al 2FA y a las claves de API — aquellos controlan
**quién entra**, esto controla **qué sigue dentro**: si alguien te robó la contraseña y
entró antes de que activaras el 2FA, cambiarla no lo echa; cerrar su sesión sí.

- ⚠️ **ABP open source trae la entidad `IdentitySession`, su repositorio y la tabla, pero
  NADA las llena**: poblarlas es de Account Pro. Aquí se construye esa pieza
  (`RegistradorDeSesiones`), enganchada en los DOS caminos de entrada: login normal y
  segundo factor.
- Al registrar hay que **refirmar la cookie** con `SignInWithClaimsAsync`, porque
  `PasswordSignInAsync`/`TwoFactorSignInAsync` ya la emitieron sin el claim.
- ⚠️⚠️ **LA TRAMPA GRANDE**: el endpoint de autorización de OpenIddict **no reutiliza el
  principal de la cookie**, llama a `SignInManager.CreateUserPrincipalAsync(user)` y lo
  reconstruye *desde el usuario*. Los claims añadidos al iniciar sesión se pierden, así que
  **el access token sale sin `session_id` y revocar no corta al SPA** — solo a las páginas
  MVC. Se arregla con un `IAbpClaimsPrincipalContributor` que copia el claim del principal
  HTTP actual (`SessionIdClaimContributor`).
  - **No se ve en ninguna prueba de la API**: hay que decodificar un access token real y
    mirar sus claims. Así se encontró.
- La validación va en un middleware, **después de `UseAuthentication`** y antes de
  `UseAuthorization`. Si la sesión ya no existe **la petición sigue como anónima** en vez de
  responder un 401 a mano: así el resto del pipeline hace lo que ya sabe y no hay un camino
  de error nuevo que mantener (ni la trampa de `UseErrorPage`).
- `LastAccessed` se refresca **como mucho cada 5 min**: el camino caliente aquí son TODAS
  las peticiones autenticadas, y escribir en cada una convertiría toda lectura en escritura.
- **Logout borra su fila** y un worker purga las inactivas a los 30 días. Sin eso la lista
  se llena de sesiones muertas, y una lista ilegible no sirve para lo único que se le pide:
  reconocer lo que NO deberías ver.
- ⚠️ «Cerrar las demás» **falla si no se puede identificar la sesión propia**, en vez de
  borrarlas todas: borrarlas todas echaría también a quien pulsó el botón.

