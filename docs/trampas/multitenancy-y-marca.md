# Multi-tenancy, espacio por inquilino (/app/{nombre}) y marca (branding)

> Parte de `CLAUDE.md` — extraído para no cargar todo el historial de trampas en cada sesión. Ver el índice en la raíz.

## 6.96 Multi-tenancy: cómo funciona de verdad

`MultiTenancyConsts.IsEnabled = true` y el CRUD de tenants lo trae ABP entero
(`/tenant-management`, con alta del administrador del tenant incluida). **Verificado de
punta a punta**: crear un tenant, entrar como su administrador desde el navegador y
comprobar que no ve los datos del host.

- **El tenant se elige en el propio login**, con el enlace «Cambiar» que trae el módulo de
  cuentas de ABP. No hace falta un subdominio por tenant ni configuración extra.
- Al crear un tenant, ABP **siembra su administrador y sus permisos** (28 concesiones en la
  prueba). No hay que hacer nada a mano.
- ⚠️ **El tenant del USUARIO manda sobre la cabecera `__tenant`.** Es el orden de
  resolución de ABP (`CurrentUserTenantResolveContributor` va primero), y es lo correcto:
  significa que **no puedes salirte de tu tenant mandando una cabecera**.
  - Corolario al probar: una clave de API pertenece a un usuario, y por tanto **a un
    tenant**. Mandar `__tenant` con la clave del host NO cambia nada — la petición sigue
    siendo del host. Para probar el aislamiento hace falta una clave **de cada** tenant, y
    confundir esto hace parecer que hay una fuga cuando no la hay.
- Comprobado con datos reales: el informe de productos —que es **SQL crudo**, donde el
  filtro de tenant está escrito a mano— devuelve al tenant solo lo suyo y al host solo lo
  suyo. Las claves de API, igual.

## 6.965 Un espacio por inquilino: `/app/{nombre}`

Cada inquilino tiene su propia dirección —`https://sitio/app/acme`— con su login, su marca y
sus datos. El host se queda en la raíz. **Es una subruta, no un subdominio**: así no hace
falta certificado comodín, ni DNS por inquilino, ni tocar el emisor de OpenIddict.

Toda la forma de la URL vive en `angular/src/app/tenancy/ruta-de-inquilino.ts`. El resto de la
aplicación no se enteró: **ninguna ruta ni ningún `routerLink` cambió**, porque el prefijo lo
absorbe `APP_BASE_HREF`. `index.html` conserva `<base href="/">` para que los bundles se
sigan sirviendo desde la raíz.

- ⚠️⚠️ **Los `provideAppInitializer` de Angular corren EN PARALELO.** Se *empiezan* en orden,
  pero en cuanto uno hace `await` los demás siguen. Resolver ahí el inquilino dejaba que el
  inicializador de ABP pidiera `/application-configuration` **antes**, sin la cabecera
  `__tenant`: el SPA arrancaba con los permisos, los ajustes y la marca **del host** dentro
  del espacio de un inquilino, y no fallaba nada. Por eso se resuelve en `main.ts`, **antes de
  `bootstrapApplication`** (`resolver-espacio.ts`), y el inicializador solo hace cosas
  síncronas.
- ⚠️ **El `environment` se modifica IN SITU**, no se copia. `appConfig` ya le pasó esa
  referencia a `provideAbpCore` al importarse el módulo, y el inicializador de ABP hace luego
  `setState(options.environment)` sobre ella. Cualquier copia acababa machacada: la petición
  de autorización salía con la `redirect_uri` de la raíz y sin `__tenant`, y tras iniciar
  sesión el usuario aterrizaba en el panel del host.
- ⚠️ **`__tenant` en `/connect/authorize` tiene que ser el GUID, no el nombre.**
  `OpenIddictSupportedLoginModel.OnGetAsync` hace `Guid.Parse(tenant)` a secas (verificado en
  `refs/abp`): con un nombre, la pantalla de login responde **500** con un `FormatException`
  en el servidor de autenticación, donde no se ve desde el navegador. Curiosamente el
  resolutor general de ABP sí acepta las dos formas; esa pantalla concreta, no.
- ⚠️ **Cada espacio es una `redirect_uri` distinta y OpenIddict las compara EXACTAS** (no
  admite comodines, por diseño del protocolo). Las mantiene al día
  `SincronizadorDeRedirecciones`, que **reconstruye la lista entera** en vez de ir añadiendo:
  así renombrar un inquilino no deja registrada la dirección vieja ni falta la nueva. Corre
  al crear/renombrar/borrar y una vez al arrancar.
  - ⚠️ Y lee `App:AngularUrl`, **no** `OpenIddict:Applications:…:RootUrl`: esa sección solo
    existe en la configuración del **DbMigrator**, no en la del Host. Leerla sin respaldo hacía
    que la sincronización se saltara entera **en silencio** — ningún error, ninguna traza, y
    unos espacios que no podían iniciar sesión.
  - La URL se construye **dos veces**, en C# y en TypeScript. Sobra una barra final y ese
    inquilino deja de entrar. Hay un test que fija la forma (`RaizDelEspacioTests`).
- ⚠️⚠️ **La cookie `__tenant` contaminaba el login del host.** La deja el login de un
  inquilino, vive en el dominio del servidor de autenticación y **no caduca con la sesión**:
  después de entrar en `/app/acme`, la pantalla de login de la raíz seguía diciendo «acme» y
  el superadministrador se autenticaba contra el inquilino. Se arregla mandando `__tenant=`
  **vacío** desde el host, que `RecordarInquilinoMiddleware` interpreta como «borra la cookie
  y trata esta petición como host». Comprobado con `curl`: con la cookie puesta el recuadro
  dice «acme»; con `__tenant=` vacío dice «Not selected» y la cookie se borra.
- **El selector «Cambiar» se oculta** dentro de un espacio (CSS desde el layout hook, sin
  sustituir la vista del tema). ⚠️ Pero eso es **interfaz, no seguridad**: el endpoint
  `/api/abp/multi-tenancy/tenants/by-name/{name}` es anónimo por diseño de ABP —y este
  proyecto lo necesita, porque es con lo que resuelve el espacio antes de que nadie inicie
  sesión—, así que probar nombres sigue siendo posible. Lo único que impediría averiguar qué
  espacios existen sería poner el **identificador** en la ruta en vez del nombre.
- **`nginx` no necesitó cambios**: su `try_files $uri $uri/ /index.html` ya devuelve el SPA
  para `/app/loquesea`.
- El aislamiento real lo sigue garantizando ABP: `CurrentUserTenantResolveContributor` va
  **primero**, así que en cuanto hay sesión manda el inquilino del usuario y ni la URL ni la
  cookie pueden sacarte del tuyo.

### 6.965.1 Alta de un inquilino y su primer administrador

El superadministrador da de alta pidiendo **solo el nombre** (`/tenants`) y obtiene un
**enlace de activación** con el que el inquilino crea su propio administrador
(`/app/{nombre}/activar?token=…`).

Existe porque `TenantCreateDto` de ABP **exige** correo y contraseña del administrador: quien
crea la organización acabaría eligiendo —y conociendo— la contraseña de otra empresa. Aquí el
alta usa una contraseña aleatoria que **no se le enseña a nadie** y la de verdad la pone el
inquilino.

- ⚠️ **El enlace no se puede enviar por correo**: `IEmailSender` es `NullEmailSender`. Se
  copia del panel y se entrega a mano — por eso el token dura poco (7 días) y se puede
  regenerar.
- **Vale de un solo uso**, con caducidad, y en la base solo su **hash SHA-256** (mismo
  criterio que las claves de API: 256 bits aleatorios no necesitan un hash lento, pero sí que
  sea determinista para buscar por él).
- **Se consume ANTES de tocar al usuario.** Si algo falla después —una contraseña que no pasa
  la política— el enlace ya no vale y hay que pedir otro. Es deliberado y tiene coste: un vale
  «de un solo uso» que a veces vale dos veces no es una garantía. Hay un test.
- ⚠️ La activación va **dentro de `CurrentTenant.Change(...)`**. Sin eso, la búsqueda del
  usuario `admin` miraría en el host y le cambiaría la contraseña **al administrador del
  host**. Hay un test de aislamiento.
- ⚠️ **`UserManager` DEVUELVE los errores, no los lanza.** Sin traducir `IdentityResult` a
  excepción, una contraseña floja o un nombre ya ocupado se perderían en silencio y el
  administrador se quedaría con la contraseña aleatoria que nadie conoce.
- ⚠️ **Hay que BORRAR el nombre de pila que siembra ABP**, que es literalmente `"admin"`. La
  barra superior de LeptonX pinta `user.name || user.userName`, así que el administrador de un
  inquilino veía «admin» arriba a la derecha por más que se hubiera puesto otro nombre — la
  API decía `ana` y la pantalla decía `admin`. Se encontró comparando las dos.
- ⚠️⚠️ **El manejador de eventos de inquilino NO puede abrir una unidad de trabajo nueva.**
  Los eventos de entidad de ABP se publican **antes** de confirmar la transacción que los
  provocó: con `requiresNew: true` se abre otra transacción que, por aislamiento, **no ve** el
  inquilino recién insertado. El sincronizador de redirecciones concluía que no había nada que
  cambiar y no escribía nada — sin error, sin traza, y con un inquilino nuevo incapaz de
  iniciar sesión. Va con `requiresNew: false`, uniéndose a la ambiental.

### 6.965.2 ⚠️⚠️ `MultiTenancySides` vale `Both` por defecto

**Un permiso declarado sin el lado queda disponible también para los inquilinos**, y ABP
concede al rol `admin` de cada inquilino nuevo **todos** los permisos disponibles para su
lado. Como ninguna de las definiciones de este proyecto lo declaraba, el administrador de un
inquilino acababa con **exactamente los mismos permisos que el superadministrador**: bloquear
IPs de toda la instalación, leer la cola de trabajos entera —con los argumentos a la vista— y
cambiar el bot y los chats de los avisos.

Se descubrió entrando como administrador de un inquilino y comparando `grantedPolicies` con
los del host: **la diferencia era ninguna**. No se ve leyendo el código —lo escrito es un
permiso normal— ni rompe ningún test funcional: todo sigue haciendo lo que dice.

- **La regla: si lo que hay detrás es de la INSTALACIÓN, va `MultiTenancySides.Host`.**
  Métricas del servidor, cola de trabajos, copias, bloqueo de IPs y explorador de BD lo son,
  aunque se miren desde dentro de un inquilino. Lo del inquilino —sus productos, sus claves,
  su marca— se queda en `Both`.
- **Surte efecto de inmediato, sin tocar la base**: `PermissionChecker` compara el lado
  **antes** de mirar las concesiones (verificado en `refs/abp`), así que las filas ya
  sembradas en `AbpPermissionGrants` quedan inertes. Siguen ahí: volver a `Both` las
  reactivaría.
- Lo vigila `PermisosDeInquilinoIntegrationTests`, y su tercer test es el que importa: un
  permiso **nuevo sin clasificar** sale en rojo. Los otros dos solo cubren lo que existía el
  día que se escribieron, que es exactamente cómo se coló el fallo. Comprobado que se pone
  rojo al quitarle el lado a `IgasConsole.Jobs`.
- Verificado también en el navegador: el administrador de un inquilino ve en el menú solo
  Identity, claves de API y marca, y escribiendo a mano `/security`, `/jobs`, `/metrics`,
  `/system-status` o `/tenants` se queda fuera.
  - ⚠️ Al comprobarlo, **da tiempo suficiente tras cada recarga**: con 3,5 s el guardia de
    permisos aún no tenía la configuración y `/branding` parecía bloqueada también. Con 9 s
    se ve lo correcto. Un falso positivo así invita a «arreglar» algo que funciona.

## 6.97 Marca por inquilino (nombre, logo y color)

Pantalla `/branding`, permiso `IgasConsole.Branding`. Cada inquilino pone su nombre, su logo y
su color de acento, y se ven **también en la pantalla de login** del Host. El host edita el
valor por defecto que heredan los que no hayan puesto el suyo.

**Casi todo el trabajo lo hace ABP y no se ve en el código**: los ajustes ya son por
inquilino porque `TenantSettingValueProvider` va registrado entre el global y el de usuario,
y con `isVisibleToClients: true` viajan dentro de `/api/abp/application-configuration` **ya
resueltos para el inquilino actual**. No hay tabla nueva ni resolución propia. Justo por eso
hay un test de aislamiento: no queda código que leer para comprobar que funciona.

- ⚠️⚠️ **El logo NO puede ser un ajuste: `AbpSettings.Value` es `varchar(2048)`**
  (`SettingConsts.MaxValueLength`, verificado en `refs/abp`), y un `data:` URL de una imagen
  no cabe ni de lejos. Los bytes van a un BLOB tipado y el ajuste guarda solo una **marca de
  versión** de 8 caracteres. Esa marca hace dos cosas de una vez: dice si hay logo sin tener
  que pedirlo, y va en la URL como `?v=`, lo que permite cachear la imagen para siempre y
  aun así ver el cambio al instante.
- **El contenedor de BLOBs ya es multi-tenant** (`IsMultiTenant = true` por defecto), así que
  la clave es la constante `"logo"` y cada inquilino tiene el suyo. Para leer el de otro hay
  que envolver la lectura en `ICurrentTenant.Change(id)`.
- ⚠️ **El color es un control de SEGURIDAD, no cosmética.** Acaba escrito literalmente dentro
  de un `<style>` en la página de login y dentro de `style.setProperty`. Sin validar, alguien
  podría guardar `red } body { … } .x {` e **inyectar CSS** en la pantalla de todos los
  usuarios de su inquilino. `ColorDeMarca` es una **lista blanca de forma exacta**
  (`#RRGGBB`), no un intento de limpiar caracteres peligrosos. Hay tests con los intentos de
  escape.
- ⚠️ **`ISettingProvider` devuelve el valor RESUELTO**, donde «lo puso este inquilino» y «lo
  heredó del host» se ven igual. Para el formulario hay que pedirlo a un **nivel concreto**
  (`GetOrNullForCurrentTenantAsync(..., fallback: false)`). Usar el resuelto tenía dos
  efectos silenciosos: el campo aparecía relleno con el nombre heredado —de modo que guardar
  sin tocar nada lo convertía en propio— y vaciarlo parecía no hacer nada.
- ⚠️⚠️ **La variable CSS del acento NO es la misma en los dos lados.** En el Angular manda
  `--lpx-brand` (14 usos en las hojas del tema; `--lpx-primary` allí no la consume nadie); en
  las páginas MVC es al revés, manda `--lpx-primary`, que alimenta a `--bs-primary`. Y
  `.btn-primary` **no lee ninguna de las dos**: Bootstrap le da a cada botón sus propias
  variables (`--bs-btn-bg`…) con el color ya compilado. Se descubrió **midiendo
  `getComputedStyle`** en el navegador: las variables cambiaban de valor y los botones
  seguían exactamente del mismo color. Se redefinen las cuatro más una regla para
  `.btn-primary` (los tonos de hover salen de `color-mix`, no de tres ajustes más).
- ⚠️ **`AbpTitleStrategy` reescribe `document.title` en CADA navegación** con `::AppName`, el
  nombre del proyecto. Asignar `document.title` a secas no basta: la pestaña se pone bien al
  guardar y vuelve al nombre de la plantilla al cambiar de pantalla. Se sustituye con
  `withTitleStrategy(TituloConMarca)`, heredando de la clase de ABP para conservar el formato
  `Ruta | Aplicación` y el re-titulado al cambiar de idioma.
- ⚠️ **El componente sustituto de la marca NO entra en el primer render.**
  `IfReplaceableTemplateExistsDirective` crea el `ng-template` del logo en su
  `ngAfterViewInit`, y para entonces `lpx-navbar` ya resolvió su consulta de contenido: solo
  a partir de la primera navegación aparece `app-marca`. Por eso se hacen **las dos cosas**:
  se registra el componente en `Theme.LogoComponent` (que es lo único reactivo, repinta al
  guardar) y además se provee `LOGO_APP_NAME_TOKEN` con una fábrica, para que el componente
  de serie ya enseñe el nombre correcto justo después de entrar.
  - `LOGO_URL_TOKEN` se deja **vacío** a propósito: con una URL, `BrandLogoComponent` toma la
    rama que pinta solo el logo y deja de pintar el nombre. El logo entra por la variable CSS.
- **El endpoint del logo es anónimo, y tiene que serlo**: la primera pantalla que lo enseña
  es la de login, y quien lo pide es un `background-image`, que no manda `Authorization`. El
  tipo se deduce de los **bytes** (nunca del `Content-Type` declarado) y va con `nosniff`:
  servir un HTML que alguien subió sería XSS almacenado sin autenticar. **SVG queda fuera.**
  - El inquilino va **siempre explícito en la URL** aunque la sesión o la cookie ya lo digan:
    la respuesta se marca `immutable`, y con la misma URL para todos una caché intermedia
    podría servirle a un inquilino el logo de otro.
  - ⚠️⚠️ **Su 404 lleva cuerpo y `ContentType`** — tercera vez que muerde la trampa de
    `UseErrorPage()` (§5), después del 429 del rate limiting y el 403 del bloqueo de IPs. Un
    `NotFound()` a secas daba **404 en local y `302` en producción**; el `background-image`
    que pide un logo inexistente seguía el redirect y se bajaba la página de error entera, y
    una caché por el camino puede guardar ese redirect. Se vio **comprobando producción tras
    desplegar**: en Development ese middleware ni existe, así que los tests y el `dotnet run`
    local no lo pueden cazar. **Regla práctica: todo 4xx propio escribe cuerpo.** (Desde
    §5.0.1 hay una red que lo hace igualmente; esta regla se mantiene porque un cuerpo escrito
    a propósito dice más que el genérico.)
  - Esa URL está construida **dos veces**, en `LectorDeMarca` (C#, para el login, que es HTML
    del servidor) y en `logo-url.ts` (para el SPA, que no puede llamar al application service
    porque exige permiso). Un test de integración fija la forma exacta.
- ⚠️ `IBrandingProvider` declara propiedades **síncronas** y las vistas lo inyectan con
  `@inject`, así que hay un `AsyncHelper.RunSync`. Está acotado porque ABP ya cachea los
  ajustes (`SettingManagementStore` sobre `IDistributedCache`): es un acceso a caché, no a la
  base. Es el mismo recurso que usa ABP en su `MvcCachedApplicationConfigurationClient`.
- ⚠️ **En la pantalla, subir el logo NO puede releer el formulario.** Estaba haciéndolo, y
  quien escribía un nombre nuevo y después elegía un logo veía su texto volver en silencio al
  anterior — y al pulsar Guardar se guardaba ese. Se detectó automatizando el navegador: el
  logo entraba y el nombre no, sin ningún error.
- El **nombre por defecto** es el texto localizado `AppName`, el que ya usaba el proveedor de
  marca original: una instalación que no abra nunca esta pantalla se sigue viendo igual.

### 6.97.1 Trampa de los tests de integración (afecta a TODOS)

⚠️ **xUnit construye la clase de test —y con ella la aplicación ABP entera— ANTES de llamar a
`InitializeAsync`**, que es donde corre el `Respawn`. O sea que la aplicación arranca viendo
los datos del test anterior, y lo que lea durante el arranque se le queda cacheado; el
borrado de un instante después limpia las filas pero no la caché.

Con datos normales da igual, porque cada test escribe los suyos. Con los **ajustes** no: ABP
los cachea en bloque y un ajuste de otro test se cuela sin que nada falle. Pasó de verdad —
dos tests de aislamiento fallaban viendo un nombre que ese test nunca escribió. Por eso
`IntegrationTestBase.DisposeAsync` **también** limpia: así la aplicación del test siguiente
siempre nace sobre una base vacía.

### 6.965.3 Entrar como el administrador de un inquilino (suplantación)

Botón «Entrar como admin» en `/tenants`. Permiso propio `IgasConsole.Tenants.Impersonate`
(**Host**), aparte del CRUD: crear una organización y entrar a mirar sus datos con la cara de
otro no son la misma responsabilidad.

ABP open source **no trae suplantación** (es de Account Pro), pero el framework deja puesta
toda la fontanería: los cuatro claims `AbpClaimTypes.Impersonator*`, los campos de
`AuditLogInfo` que guardan quién suplantó a quién, y `CurrentUserDto`, que ya se los enseña al
navegador. O sea que **el rastro de auditoría sale gratis** — cada acción hecha suplantando
queda registrada con las dos identidades. Lo que falta, y es lo que hay aquí, es el flujo.

- **Dos saltos**: el SPA pide un **vale** a la API con su token (ahí se comprueba el permiso) y
  el navegador lo canjea en el servidor de autenticación, que firma la cookie como el
  administrador del inquilino.
  - ⚠️ **El vale existe por CSRF.** El segundo salto tiene que ser una navegación de primer
    nivel para que viaje la cookie de Identity (`SameSite=Lax`), y eso lo provoca cualquier
    enlace externo. Sin el vale bastaría con que el superadministrador pulsara un enlace
    cualquiera para acabar suplantando sin enterarse y seguir trabajando creyendo que es él.
  - Vive en **caché** (60 s, un solo uso, solo el hash) y no en una tabla: dura un minuto y no
    es un registro que nadie vaya a consultar. Tercer uso del patrón, tras la activación
    (§6.965.1) y las claves de API (§6.05). Guarda **quién lo pidió**, y el canje lo exige: si
    no, el vale —que viaja en una URL y queda en el historial— lo podría usar otra sesión.
- ⚠️⚠️ **Al administrador se le busca por su ROL, nunca por su nombre.** Lo primero que se
  escribió fue `FindByNameAsync("admin")`, que es como lo siembra ABP. Está mal justo al revés
  de como parece: la activación hace `SetUserNameAsync` con el nombre que elige el inquilino,
  así que habría funcionado **solo con los espacios sin activar** — y fallado con todos los
  demás, que son los únicos donde tiene sentido entrar. La búsqueda es **compartida** entre el
  permiso y el canje, para que no puedan discrepar.
- ⚠️⚠️ **Los tokens del SPA viven por ORIGEN, no por ruta**, y eso muerde en los DOS sentidos.
  El panel del host y todos los espacios comparten `localStorage`, así que al aterrizar tras
  el canje el SPA seguía teniendo los tokens del superadministrador, se creía autenticado y ni
  pedía otros: el navegador acababa de vuelta en el panel del host, con la cookie del inquilino
  ya firmada y nadie usándola. Y al volver pasaba lo mismo al revés — la URL decía que habías
  vuelto y el token seguía siendo el de la otra empresa, con la franja puesta.
  - Se resuelve con una ruta de paso, **`/entrando`**, a la que redirigen los dos caminos:
    tira los tokens locales con `oauth.logOut(true)` y pide otros. ⚠️ El `true` es
    imprescindible: sin él iría al endpoint de cierre de sesión del servidor y **mataría la
    cookie que el canje acaba de crear**.
- ⚠️⚠️ **El contribuidor de claims se copiaba las marcas a sí mismo al volver.**
  `SignInAsync` construye el principal con la fábrica de ABP, que ejecuta los
  `IAbpClaimsPrincipalContributor` — y el nuestro copia las marcas desde `HttpContext.User`,
  que en esa petición es todavía el principal suplantado. Resultado: el superadministrador
  volvía a su cuenta y su token seguía diciendo que suplantaba. Se limpia `HttpContext.User`
  antes de refirmar.
- ⚠️ **Sin el contribuidor no hay rastro y no se ve.** Misma trampa de §6.7: el endpoint de
  autorización reconstruye el principal **desde el usuario** y pierde los claims de la cookie.
  Lo que se pierde con ellos es lo que hace aceptable la función: la auditoría deja de guardar
  quién suplantó —cada acción queda a nombre del administrador del inquilino, o sea que
  **señala a la persona equivocada**— y el SPA deja de pintar la franja de aviso.
  - **Hay que decodificar un access token real.** La cookie sí lleva los claims y las páginas
    MVC funcionan; en una prueba de la API no se ve nada. Y el token **no está en
    `localStorage`** (solo el refresh): se captura de la cabecera `Authorization` de una
    petición.
- ⚠️ **La entrada del registro de seguridad se escribe FUERA del inquilino.** Dentro del
  `Change`, la fila se guarda con el `TenantId` del inquilino y solo la vería su propia gente
  —que además no puede abrir `/security` (§6.965.2)—. Quien necesita leer «quién entró en el
  espacio de este cliente» es justo el del host, y desde allí era invisible. Se detectó mirando
  la pantalla: aparecían las vueltas y no las entradas.
  - El DTO del registro no exponía las propiedades extra, así que la fila decía que alguien
    entró en un espacio **sin decir en cuál**. Se añadió `Detalle`, aplanado en el servidor.
- **La franja de aviso no es decoración**: sin ella se está mirando la casa de otra empresa con
  una pantalla idéntica a la propia. Lleva colores fijos y **no** los del tema — la marca que
  se ve es la del inquilino, y un aviso con su color de acento se confundiría con su interfaz.
- **No se encadena** y **solo desde el host**: suplantando ya no eres tú, y el rastro guarda un
  suplantador. La comprobación de «solo host» es redundante con el lado del permiso, y se deja
  puesta precisamente por eso — es lo único que quedaría si alguien devuelve ese permiso a
  `Both`.

### 6.965.4 ⚠️⚠️ Dar de baja a un inquilino NO le quita el acceso

Borrar un inquilino en ABP hace **una sola cosa**: `TenantAppService.DeleteAsync` borra su fila
y punto (verificado en `refs/abp`). Y el borrado es **blando** — `Tenant` es
`FullAuditedAggregateRoot`—, así que la fila se queda con `IsDeleted = true` y **en todo el
framework no hay un solo manejador que escuche ese borrado**.

Nada de eso está mal: la baja es **reversible a propósito** y sus datos siguen ahí. Purgarlos
la haría irreversible sin que nadie lo hubiera pedido. Lo que no puede sobrevivir a la baja es
el **acceso**, y ahí estaba el fallo.

- ⚠️⚠️ **Una clave de API seguía autenticando después de dar de baja al inquilino.** El camino
  de `X-Api-Key` no preguntaba por el inquilino **en ningún momento**, y las tres condiciones
  que sí miraba se cumplen todas:
  1. `FindByHashAsync` encuentra la fila — lleva `IgnoreQueryFilters()`, y **tiene** que
     llevarlo: la clave llega antes de saber de qué inquilino es (§6.05).
  2. `EsUtilizable` es `true` — nadie la revocó, y dar de baja al inquilino tampoco lo hace.
  3. `FindByIdAsync` encuentra al usuario — su fila sigue intacta.

  Resultado: la organización desaparecía del panel y sus integraciones seguían leyendo y
  escribiendo sus datos. **Sin caducidad**, porque una clave no se apaga sola.
- **La comprobación que faltaba va contra `ITenantStore.FindAsync(id)`**, no contra el
  repositorio. Es lo mismo que consulta la resolución de inquilino al entrar por la web —o sea,
  **una sola definición de «está de alta»** para los dos caminos— y va **cacheado**, que
  importa porque esto corre en cada petición que traiga clave. ABP invalida esa caché al
  borrar (`TenantConfigurationCacheItemInvalidator`, escucha `EntityChangedEventData<Tenant>`),
  así que la baja surte efecto **al instante** y no cuando caduque una entrada.
- ⚠️ **`TenantId == null` es el host, no «sin inquilino de alta».** Tratar el nulo como baja
  habría dejado sin acceso a todas las claves del superadministrador de golpe. Hay un test.
- ⚠️ **La regla se sacó del middleware al `ResolutorDeClaveDeApi` (capa Application) para poder
  probarla.** El middleware vive en el Host, que los tests de integración **no cargan**: la
  decisión más sensible del proyecto era justo la única que ningún test alcanzaba. El
  middleware se queda con la fontanería HTTP (leer la cabecera, construir el principal, anotar
  el uso, escribir el 401). Al moverla se cubrieron de paso «revocada» y «no existe», que
  viajaban dentro del Host sin un solo test.
- **Se encontró leyendo, no fallando**, y por eso el test se escribió **antes** del arreglo:
  primero se comprobó que las cuatro condiciones se cumplían de verdad contra PostgreSQL, y
  después se verificó que el test se pone rojo al quitar la comprobación nueva.
- **La regla vive en un solo sitio**, `InquilinosDeAlta` (capa Application). La preguntan ya
  dos caminos —el de las claves y el vigilante de caducidades— y escrita dos veces acabaría
  respondiéndose de dos formas: es exactamente el tipo de comprobación que se copia y luego
  solo se corrige en un lado.
  - `ApiKeyExpiryWatchWorker` la usa para **callarse** con los inquilinos dados de baja. Su
    consulta lleva `IgnoreQueryFilters()` —y tiene que llevarlo, porque el worker corre sin
    inquilino (§6.02.2)—, así que también traía esas claves. Avisar de ellas es ruido puro:
    ya no autentican, y el aviso llena una campana que nadie puede abrir, porque para verlo
    hay que poder entrar.

