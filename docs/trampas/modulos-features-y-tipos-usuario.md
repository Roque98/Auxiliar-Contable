# Módulos por cliente (features/permisos), tipos de usuario y coste de despliegue

> Parte de `CLAUDE.md` — extraído para no cargar todo el historial de trampas en cada sesión. Ver el índice en la raíz.

## 6.966 Módulos por cliente: features y permisos son ejes DISTINTOS

Un solo despliegue sirviendo verticales distintos (una escuela, un restaurante, una óptica) se
apoya en **dos** mecanismos, y confundirlos es el error caro:

```
Feature (¿este CLIENTE lo tiene?)  →  Permiso (¿esta PERSONA puede?)  →  acción
```

- **Feature** = qué módulo tiene contratado un **inquilino**. Se activa desde la pantalla de
  features de ABP. `Volo.Abp.Features` es del **framework**, no de Pro, y el proyecto ya traía
  cableado `FeatureManagement` en las cinco capas y `@abp/ng.feature-management` instalado.
- **Permiso** = qué puede hacer una **persona** dentro de un cliente que ya tiene el módulo.

**Se atan con `permiso.RequireFeatures(...)`**: sin la feature, el permiso **no existe** para
ese inquilino — no aparece en su pantalla de permisos, no se le puede conceder por error y
`PermissionChecker` lo niega aunque quedara una concesión vieja en la base.

- ⚠️ **Por qué no bastan los permisos**, que es lo primero que uno piensa: un permiso nuevo
  nace **disponible** y hay que acordarse de negarlo cliente por cliente. Este proyecto ya se
  llevó ese golpe (§6.965.2). Una feature nace **apagada** (`defaultValue: "false"`): el olvido
  falla hacia el lado seguro, que es la única clase de defecto admisible en algo que separa
  clientes.
- **`isAvailableToHost: false`** en un vertical, por el mismo criterio que `MultiTenancySides`
  en los permisos: la instalación administra inquilinos, copias y seguridad — no lleva un
  restaurante.
- **Doble candado en el application service**: `[RequiresFeature]` **y** `[Authorize]`. El
  primero no sobra aunque los permisos ya declaren `RequireFeatures`: aquel gobierna qué
  permisos existen, este corta la llamada aunque alguien llegara con una concesión heredada de
  cuando el módulo sí estaba contratado.
- ⚠️ **Al probarlo, comprobar el CÓDIGO de la excepción, no solo que lance.** ABP usa
  `AbpAuthorizationException` tanto para «te falta el permiso» como para «no tienes el módulo»,
  así que un test que solo mire el tipo pasa por la razón equivocada. Y el código es
  `AtLeastOneOfTheseFeaturesMustBeEnabled`, no `FeatureIsNotEnabled`: `[RequiresFeature]`
  admite varias y toma esa rama aunque se le pase una sola. Se descubrió ejecutándolo.
- **Vertical de referencia**: `Restaurante/` (entidad `Mesa`, tabla `AppMesas`). Vive en su
  propia carpeta a propósito — si algún día hay que separarlo a su propio despliegue, lo que
  hay que mover está junto.
  - Y como toda entidad propia, **con `DbSet`**: sin él no recibe los filtros de inquilino ni
    de borrado lógico y devuelve las filas de todos los clientes sin fallar (§5). En un módulo
    que se vende por cliente, esa es la peor fuga posible.
- **Si un cliente necesita su base aparte**, ABP admite cadena de conexión por inquilino
  (`TenantConfiguration.ConnectionStrings`) **desde el mismo despliegue**: se puede empezar
  compartiendo base y mover uno después sin tocar código.

### 6.966.05 Contratar un módulo: qué pasa con los permisos

Al activar una feature a un inquilino, **su administrador ve el módulo sin que nadie le conceda
nada**. Comprobado de punta a punta en el navegador (activar → suplantar → aparece en el menú
y la pantalla funciona), y explicado por dos cosas leídas en `refs/abp`:

1. `PermissionDataSeedContributor` concede al rol `admin` **todos** los permisos del lado
   inquilino, y **no filtra por feature** (usa `GetPermissionsAsync()`, que devuelve
   definiciones sin evaluar los `StateCheckers`).
2. `PermissionChecker` evalúa el estado **antes** de mirar las concesiones, así que esa fila
   queda **inerte** mientras la feature está apagada y **se enciende sola** al activarla.

⚠️ **La condición**: el sembrador tiene que haber corrido desde que el permiso existe. Para un
módulo recién añadido eso significa **una pasada del DbMigrator** — que el despliegue ya hace
antes de intercambiar versiones. Sin esa pasada, el permiso está definido pero nadie lo tiene.

⚠️⚠️ **Al comprobarlo, hay que mirarlo DESDE DENTRO del inquilino.** Pedir los permisos del rol
`admin` como usuario del host con la cabecera `__tenant` **no sirve**: §6.96 — el inquilino del
usuario manda sobre la cabecera, así que la respuesta sigue siendo la del host, donde un
permiso `MultiTenancySides.Tenant` no existe. Parece que el permiso «no aparece» y lo que pasa
es que se está preguntando en el sitio equivocado. Costó dos vueltas.

#### 6.966.07 Cuotas por inquilino: la otra mitad de un módulo

Una feature de interruptor responde «¿tiene esto contratado?»; una **cuota** responde
**«¿cuánto?»**. Sin ellas un solo cliente puede consumir sin techo — y aquí no es teórico:
**los archivos van dentro de la base de datos** (§6.1), así que el almacenamiento es
compartido y finito.

- ABP admite **valores numéricos** en las features (`FreeTextStringValueType` con
  `NumericValueValidator`, verificado en `refs/abp`), así que se administran desde la **misma
  pantalla** que los módulos. No hizo falta nada nuevo.
- ⚠️⚠️ **Nacen en `0` = SIN LÍMITE, al revés que los módulos, que nacen apagados.** La
  asimetría es deliberada: un módulo que se encendiera solo **regala** algo que el cliente no
  contrató; una cuota que se aplicara sola le **quita** algo que ya estaba usando, y lo
  descubre chocando contra ella en mitad de su trabajo. Los dos olvidos fallan hacia el lado
  que no rompe nada, y ese lado es el contrario en cada caso. Tiene test propio porque **se
  lee justo al revés de como se comporta**.
- ⚠️ `isAvailableToHost: **true**`, también al revés que el vertical: el host no lleva un
  restaurante pero **sí consume**. Ojo: eso hace que el host *vea* la cuota, no que se le pueda
  poner una — **ABP no tiene proveedor de features para el host** (son Configuration,
  DefaultValue, Edition y Tenant). Para el host manda el valor por defecto.
- El guardián (`GuardiaDeCuotas`) está en un solo sitio porque lo usan tres servicios
  distintos y los tres tienen que responder igual. Corta con `>=` y no con `>`: se pregunta
  **antes** de crear, así que estar ya en el límite significa que el siguiente sobra.
- **El validador rechaza un valor inválido al GUARDARLO**, cosa que salió al escribir los
  tests y es mejor de lo esperado: `FeatureManager.SetAsync` lanza `FeatureValueInvalidException`.
  La tolerancia del guardián se queda como red para valores que lleguen por configuración
  —que no valida— y se prueba aparte con un doble.
- ⚠️ La cuota de claves cuenta las **utilizables**, no las filas: una revocada se conserva para
  poder auditarla (§6.05) pero ya no autentica. Si contara, el inquilino ordenado —el que
  revoca lo que deja de usar— se quedaría sin cuota antes que el que las abandona.
- ⚠️ La de usuarios **no cubre todos los caminos, y es deliberado**: no frena al administrador
  que siembra ABP ni al de la activación (§6.965.1). Son el **primer** usuario de la
  organización, y una cuota que impide empezar no protege nada.

### 6.966.08 Dar de baja de verdad: la purga

Dar de baja borra la fila y nada más, en blando (§6.965.4). Eso está bien —es reversible— pero
deja sin resolver al cliente que se fue y pide que le borren lo suyo, que en bastantes sitios
no es cortesía sino obligación. **Son dos pasos separados a propósito.**

- ⚠️⚠️ **Un purgado incompleto es PEOR que no tenerlo**, porque afirma haber borrado y quien lo
  pidió se queda tranquilo. Por eso el test que importa **no comprueba que borre**: lee el
  **esquema vivo** (`information_schema`) y falla si aparece una tabla con `TenantId` que nadie
  ha declarado — cubierta, o excluida **con su motivo escrito**. Comprobado que se pone rojo al
  quitar una tabla de la lista.
  - La enumeración inicial salió de **consultar la base**, no de la memoria: 31 tablas con
    `TenantId` más las que pertenecen a un inquilino **sin tenerlo** (`AbpSettings` y
    `AbpFeatureValues` van por `ProviderKey`; los enlaces de Telegram cuelgan del usuario).
- Se borra con `ExecuteDeleteAsync` (LINQ, no SQL crudo: §8) y **de hijo a padre**. No se
  confía en las cascadas de la base: existen para casi todo, y «casi» no sirve cuando la
  operación afirma haber borrado.
- **Confirmación escribiendo el nombre EXACTO** —ni `Trim` ni ignorar mayúsculas—. El punto no
  es validar una cadena: es obligar a mirar qué organización se está destruyendo.
- La ficha se borra en **duro**: el borrado blando dejaría la fila con el nombre del cliente
  dentro, que es uno de los datos que se pidió borrar.
- ⚠️ El botón de confirmar lleva un rojo **fijo**, no el del tema. Medido: con la marca en rosa,
  el primario salía `rgb(247,37,133)` y el «peligro» `rgb(192,13,73)` — dos tonos del mismo
  color. Mismo criterio que la franja de suplantación (§6.965.3).

### 6.966.09 Importación CSV: el camino de vuelta del export

Hace falta el **primer** día de un cliente, no el último: nadie teclea a mano el catálogo que
ya tiene en una hoja. Toda la parte que se equivoca vive en `ImportadorDeProductos`, que **no
toca la base** y por eso tiene 30 tests baratos.

- ⚠️ **Decimales con coma Y con punto.** Excel en español escribe `12,50`; en inglés `12.50`.
  Leer solo con `InvariantCulture` rechaza el archivo entero. Y con **los dos** separadores
  (`1.234,50`) se **rechaza la fila** en vez de adivinar: ahí hay separador de miles y acertar
  a medias mete un precio mil veces mayor.
- ⚠️ **Las columnas se localizan por NOMBRE, no por posición.** Por posición funciona con el
  archivo que uno mismo exportó y mete los precios en el stock con el primero que llega de
  fuera, sin avisar.
- ⚠️⚠️ **Una comilla en MEDIO de una celda es un carácter, no una apertura de campo.** Un
  nombre tan normal como `Monitor 27"` abría un campo que no se cerraba nunca y el lector se
  tragaba **el resto del archivo**: se importaban 2 filas de 6 con un único error hablando de
  un precio vacío. Se encontró probando con un CSV escrito **como lo escribiría una persona**,
  no como lo exporta la aplicación — que es justo la diferencia para la que existe un import.
- Se quita el **BOM** al leer: `EscritorCsv` lo pone a propósito (§6.2), así que un archivo
  exportado y vuelto a subir lo trae, y sin quitarlo la primera cabecera no casa con nada.
  Hay un test de **ida y vuelta** que cubre BOM, separador y escapado de una vez.
- **Un archivo a medias importa lo bueno y reporta lo malo con su número de línea.** Es el caso
  normal, no la excepción: rechazarlo entero obliga a buscar tres líneas a ciegas, y aceptarlo
  callando esconde que faltan tres cosas. Y hay un modo **«comprobar sin guardar»**.
- ⚠️⚠️ **El endpoint NO puede mezclar `IRemoteStreamContent` con un DTO.** Con un DTO al lado
  del stream, ABP genera el endpoint declarando `application/json` y **toda subida responde
  415**. Las opciones van como parámetros sueltos. Se vio comparando en Swagger con el de la
  foto de perfil, que funciona precisamente porque recibe solo el stream.
- ⚠️ Los mensajes de error **se localizan**: el importador recibe un traductor como función en
  vez de un `IStringLocalizer`, para no necesitar el contenedor de ABP en sus tests. Los tests
  comprueban la **clave** y no el texto — así no se rompen al reescribir un mensaje y dicen qué
  error se esperaba. La primera versión los tenía en español fijo y salían mezclados con la
  interfaz en inglés; se vio **en una captura**, no leyendo el código.

### 6.966.095 El catálogo como módulo, y qué NO hace `isAvailableToHost`

Productos vivía suelto: la pantalla de importar colgaba del menú sin grupo y el catálogo
—alta, informe de ventas, import— era lo único del proyecto que ningún inquilino contrataba.
Ahora es la feature `AuxiliarContable.Catalogo`, con `RequireFeatures` en sus permisos y
`[RequiresFeature]` en `ProductAppService` y `ProductImportAppService`.

- ⚠️⚠️ **`isAvailableToHost` NO es un control de acceso — es la PANTALLA.** Verificado en
  `refs/abp`: el flag lo consulta **un solo sitio de todo el framework**, `FeatureAppService`,
  para no listarle la feature al host. No aparece en `IFeatureChecker`, ni en
  `FeatureManager.SetAsync`, ni en ningún proveedor de valor. El nombre sugiere lo contrario
  y por eso está escrito aquí.
  - Corolario que sí importa: **el host puede tener features encendidas.** ABP no tiene
    proveedor de features para el host —los cuatro son Configuration (`"C"`), DefaultValue
    (`"D"`), Edition (`"E"`) y Tenant (`"T"`)—, pero
    `TenantFeatureManagementProvider.NormalizeProviderKeyAsync` convierte la clave nula en
    `CurrentTenant.Id?.ToString()`, que para el host **vuelve a ser nula**, y con esa misma
    clave la lee después `TenantFeatureValueProvider`. O sea: se direcciona al host como un
    inquilino de id nulo sobre el proveedor `"T"`.
- **Y el host NO está exento del `[RequiresFeature]`.** Sin contratar, el superadministrador
  recibe la misma negativa que cualquier inquilino. Tiene test propio
  (`El_host_SIN_contratar_el_catalogo_tampoco_entra`), y hace falta: al convertir productos
  en módulo se pusieron rojos 23 tests, lo que parecía el arnés molestando y era la puerta
  funcionando — pero esa evidencia es **incidental**, porque el día que alguien quite el
  `[RequiresFeature]` esos 23 se pondrían **verdes**, no rojos. Comprobado que el test nuevo
  se pone rojo al quitar el atributo.
- ⚠️ **El catálogo va `isAvailableToHost: true`, al revés que el vertical del restaurante**,
  y la diferencia no es un descuido: un vertical es el **negocio de un cliente**; un catálogo
  es una capacidad genérica que la propia instalación usa —el host tiene filas en
  `AppProducts`, el explorador de BD las edita, y la prueba de que el filtro de inquilino del
  SQL crudo funciona necesita **las dos mitades**, la del inquilino y la del host—. Dejándolo
  fuera, esa mitad de la prueba desaparece.
- **Los tests lo contratan a mano** (`ContratarModuloAsync` en `IntegrationTestBase`), no la
  clase base para todos: encendido de oficio, quitar el `[RequiresFeature]` no pondría rojo
  ni un test.
- El barrido de `PermisosPorVertical` cubre `AuxiliarContable.Products` sin tocarlo: la lista de
  prefijos es el dato del que tira, así que declarar la feature nueva ahí basta para que un
  permiso suyo sin `RequireFeatures` salga en rojo. Ojo con el permiso **padre**, que casi se
  escapa: ver §6.966.096.

## 6.966.1 Tipos de usuario: son PLANTILLAS, no un nivel de autorización

«Profesor», «Camarero». Al dar de alta a alguien se elige un tipo, **se traduce a roles**, y a
partir de ahí **manda el rol**. Lo que queda guardado es una etiqueta descriptiva.

- ⚠️⚠️ **Nunca se consulta el tipo para decidir permisos.** El día que aparezca un
  `if (usuario.Tipo == "Profesor")` habrá tres sistemas de autorización compitiendo —features,
  permisos y esto— y ninguno será la verdad. ABP tampoco tiene herencia de roles (verificado:
  `IdentityRole` **no tiene padre**), y esto no viene a inventarla.
- Es la generalización de algo que ABP ya trae con N=1: el flag `IsDefault` del rol más
  `IdentityUserManager.AddDefaultRolesAsync`.
- ⚠️⚠️ **Reaplicar una plantilla AÑADE y NUNCA quita.** «Sincronizar» —dejar al usuario
  exactamente con los roles de la plantilla— borraría los ajustes hechos a mano después (el
  coordinador que además da clase), y **en silencio**: nadie se entera hasta que alguien no
  puede trabajar. Esos ajustes son justamente la razón de que el tipo sea una plantilla.
- **Editar la plantilla no toca a los usuarios existentes.** Propagarlo es una acción aparte y
  visible, con recuento a la vista.
- La etiqueta va como **columna de verdad** en `AbpUsers` (`MapEfCoreProperty`), no dentro del
  JSON de `ExtraProperties`: la operación que le da sentido —«ponles a todos los profesores el
  rol que falta»— necesita **buscar** por ese campo, y dentro del JSON no se puede.

**Tres trampas que costaron una vuelta cada una:**

- ⚠️ **La capa Application no referencia EF Core**, así que `EF.Property(...)` no se puede
  escribir ahí. La consulta vive detrás de `IUsuariosPorTipo`, implementada en
  `EntityFrameworkCore`. Lo paró el compilador — la separación de capas haciendo su trabajo.
- ⚠️ **`GetAsync(id, includeDetails: true)` NO carga las colecciones hijas** salvo que se
  declare `AbpEntityOptions.DefaultWithDetailsFunc` para esa entidad (verificado en
  `refs/abp`, `EfCoreRepository`). El parámetro parece hacer algo y no hace nada: `Roles`
  llegaba vacío y aplicar una plantilla no ponía ningún rol, sin error ninguno.
- ⚠️ **Una consulta propia de `IdentityUser` necesita `.Include(u => u.Roles)`** si quien
  recibe los usuarios va a llamar a `IsInRole(...)`: sin él la colección llega en `null` y
  revienta con `ArgumentNullException` sobre «source», sin mencionar ni roles ni la consulta.

### 6.966.25 El tipo en el formulario de alta de ABP

Elegir el tipo al crear un usuario, en la propia pantalla de ABP. Son **tres piezas** y hacen
falta las tres:

1. **Declarar la propiedad** en `AuxiliarContableModuleExtensionConfigurator` → viaja por los DTO de
   ABP y la guarda su propio endpoint. Sin esto el campo se pinta y el valor se pierde.
2. **Un contribuidor de formulario** que la convierta en desplegable. Sin él ABP la pinta
   igualmente, pero como una **caja de texto pidiendo un GUID** con la etiqueta cruda
   `TipoDeUsuarioId`. Por eso la propiedad va con `UI.OnCreateForm.IsVisible = false`: si no,
   sale **dos veces**.
3. **`RolesAlPonerElTipo`** en el servidor, que aplica los roles. ABP guarda la etiqueta y no
   sabe nada de roles: sin este manejador, elegir «Profesor» crea a la persona sin un solo
   permiso — la pantalla parece funcionar y quien entra no puede trabajar.

- ⚠️⚠️ **Los contribuidores van en `createRoutes({...})` de Identity, NO como proveedores
  raíz.** Puestos en `app.config.ts` con `IDENTITY_CREATE_FORM_PROP_CONTRIBUTORS` **no se
  aplican y no falla nada**: el módulo se carga de forma perezosa y lee esos tokens de **su**
  inyector. El síntoma fue el formulario sin el campo y cero errores en consola. Se vio
  mirando el formulario.
- ⚠️ **Nada de `inject()` dentro de un contribuidor**: ABP los llama como funciones normales,
  fuera de contexto de inyección. Los servicios se piden con `data.getInjected(...)`.
- El manejador va con `requiresNew: false` (§6.965.1) y **nunca deja caer la excepción**: si
  reventara, tumbaría el alta del usuario que alguien estaba haciendo.

### 6.966.26 Tipos que trae cada módulo

`TiposSugeridos` declara qué tipos propone cada vertical. La pantalla los ofrece **solo si el
inquilino tiene ese módulo** y se aplican **a mano**, con un botón.

- **No se siembran solos al activar la feature**, y es la misma decisión de siempre: nada que
  cambie los permisos de una organización ocurre por su cuenta. Crear roles y tipos el día que
  alguien marca una casilla es justo el efecto que después nadie recuerda haber pedido.
- Los roles se declaran por **nombre** —todavía no existen— y al aplicar se crean los que
  falten, **vacíos de permisos**: quién puede hacer qué lo decide una persona.
- La feature se comprueba **otra vez** al aplicar: entre pedir la lista y pulsar el botón el
  módulo puede haberse dado de baja, y crear sus roles dejaría basura sin origen.

### 6.966.27 ⚠️⚠️ `isExtra` ya pone el `::` — la etiqueta va PELADA

El desplegable de «tipo de usuario» del formulario de alta salía **sin título**: un control
huérfano colgando debajo de «Account lockout», con sus opciones correctas y —de paso— sin
nombre accesible. Nada fallaba y en la consola no había ni un error.

La plantilla `#label` de `abp-extensible-form-prop` hace literalmente esto (verificado en
`node_modules/@abp/ng.components`):

```
@if (prop().isExtra) { {{ '::' + prop().displayName | abpLocalization }} }
@else                { {{ prop().displayName       | abpLocalization }} }
```

O sea que **con `isExtra: true` el `::` lo concatena ABP**. Las dos formas que uno escribe
primero fallan, y ninguna da error:

| lo que escribes | lo que se localiza | lo que se ve |
|---|---|---|
| `'::Menu:TiposDeUsuario'` | `'::::Menu:…'` | etiqueta **vacía** |
| `'AuxiliarContable::Tipos:Campo'` | `'::AuxiliarContable::…'` | la palabra **«AuxiliarContable»** |
| `'Tipos:Campo'` | `'::Tipos:Campo'` | ✅ «Tipo de usuario» |

- Se encontró **mirando el DOM del formulario** —el `<label for="…">` estaba ahí, con un
  `<span>` que solo tenía nodos de comentario—, no leyendo el código: desde fuera parecía
  cosa del CSS, porque el desplegable se pintaba perfectamente.
- Y la clave es **propia y en singular** (`Tipos:Campo`), no la del MENÚ, que está en plural
  porque nombra una pantalla llena de ellos.

### 6.966.28 ⚠️ El 404 al navegar justo después de un despliegue

«Al ir a usuarios y roles me da 404», con `/identity/users` respondiendo **200** y los 48
trozos de la versión desplegada **todos** en su sitio. No era el servidor.

Las pantallas se cargan de forma perezosa, así que el `main.js` que un navegador ya tiene
cargado guarda los nombres de los trozos **con el hash de esa versión**. Al desplegar, esos
archivos dejan de existir: quien tuviera la pestaña abierta y navegue a una pantalla que aún
no había visitado pide un archivo que ya no está y recibe un **404**. Un refresco lo arregla
— el problema es que nadie sabe que tiene que refrescar.

- **La caché NO es la causa y está bien puesta** (comprobado): nginx sirve `index.html` con
  `no-store` y los bundles con hash como `immutable`. Conviene mirarlo igualmente, porque un
  `index.html` cacheado produce exactamente el mismo síntoma.
- ⚠️⚠️ **El `ErrorHandler` global NO se entera.** El Router **atrapa** el fallo del `import()`
  de una ruta perezosa y lo emite como `NavigationError`. El primer arreglo fue un
  `ErrorHandler` y no hacía nada; se vio simulando el despliegue —siete trozos servidos como
  404, cero recargas—. El gancho correcto es `withNavigationErrorHandler`.
- ⚠️ **Y no basta con mirar `ChunkLoadError`**: ese nombre es de webpack. El *application
  builder* de Angular usa `import()` nativo y lo que llega es un
  `TypeError: Failed to fetch dynamically imported module`. Reconociendo solo el primero, el
  arreglo no haría nada justo en el constructor que usa este proyecto. Tiene tests con los
  mensajes reales de los dos.
- **Recarga UNA sola vez**, con marca en `sessionStorage`: si tras recargar el trozo sigue sin
  cargar —red caída, `dist` incompleto— repetir dejaría un bucle de recargas del que no se
  sale ni cerrando la pestaña.
- ⚠️ **La recuperación está sin verificar de punta a punta, y está escrito así en el código.**
  Simularlo en local exige que la aplicación esté montada y que la navegación sea del router;
  cada intento se rompió por el arnés y no por el código (`page.goto` es carga completa, el
  menú de LeptonX no expone `href`, las tarjetas de la portada no se pintan sin datos). **La
  prueba fiel es la real**: dejar una pestaña abierta, desplegar, y navegar a una pantalla no
  visitada.

### 6.966.3 Las pantallas: ABP ya trae dos de las tres

**No se escribió ninguna pantalla de permisos ni de features.** ABP trae las dos como
componentes **standalone**, ya traducidas y con su comprobación de permiso en el servidor:

| Qué | Componente de ABP | Se abre desde |
|---|---|---|
| Activar módulos de un inquilino | `<abp-feature-management providerName="T" [providerKey]="tenantId">` | `/tenants` |
| Permisos de un rol | `<abp-permission-management providerName="R" [providerKey]="rolName">` | `/tipos`, y ya estaba en `/identity/roles` |

Los nombres de proveedor son constantes verificadas en `refs/abp`: `"T"` inquilino, `"R"` rol,
`"U"` usuario, `"C"` cliente OAuth. Escribir pantallas propias habría dado **dos definiciones
de lo mismo** condenadas a discrepar.

- El botón de permisos vive en los **chips de rol** de `/tipos`: es donde uno se pregunta «¿y
  qué puede hacer este rol?».
- El permiso para activar módulos es el de ABP (`FeatureManagement.ManageHostFeatures`), no
  uno propio: la pantalla que se abre ya lo comprueba, y un permiso paralelo daría dos
  respuestas a la misma pregunta.
- ⚠️ Los modales se **destruyen al cerrarse** (`*ngIf` sobre la entidad elegida). Dejarlos
  montados conserva el estado del anterior y al reabrirlos con otro inquilino se ve un
  instante lo que no es.
- ⚠️ **La ruta que genera ABP no se adivina**: `ReaplicarATodosAsync(Guid tipoId)` sale como
  `POST /api/app/tipo-de-usuario/reaplicar-aTodos/{tipoId}` — con esa mayúscula en medio y el
  parámetro **detrás** del verbo, no `{id}/reaplicar-a-todos`. Es la regla de §6.2 y la
  primera versión del servicio Angular se la saltó: el botón habría dado 404 sin aviso previo.

### 6.966.2 ⚠️ El esquema de los tests se crea FUERA de la aplicación ABP

`PostgresFixture` monta el esquema con un `AuxiliarContableDbContext` **construido a mano** y
`EnsureCreated`. Consecuencia: **nada de lo que configuran los módulos al arrancar está en ese
modelo** — y eso incluye las propiedades extendidas mapeadas con `MapEfCoreProperty`.

Sin llamar a `AuxiliarContableEfCoreEntityExtensionMappings.Configure()` en el fixture, la tabla se crea
sin esa columna y **todos** los tests del ensamblado fallan a la vez en 1 ms con
`42703: no existe la columna a.TipoDeUsuarioId`, en el constructor de la clase de test. El
error no menciona ni los tests ni las extensiones, y parece un problema de base de datos
cuando es de modelo.

### 6.966.2b ⚠️ La base de test inválida, que se disfraza de regresión

Una corrida interrumpida a mitad de un `DROP DATABASE` deja la base marcada como
**inválida** en PostgreSQL (`pg_database.datconnlimit = -2`) y nadie puede volver a
conectarse a ella — ni siquiera para comprobar si existe.

- ⚠️⚠️ **Lo caro no es el fallo, es cómo se presenta.** `EnsureDeletedAsync` se queda
  esperando y acaba lanzando `TimeoutException: Timeout during reading attempt` envuelto en
  *«likely due to a transient failure»*, con una pila entera de Npgsql y **ni una palabra de
  la causa**. Se lee como contención o como una base caída. Y como los tests que se lleva por
  delante son los que tocaron esa base, se lee sobre todo como **una regresión del código que
  se acaba de escribir** — que es exactamente lo que pasó: media hora persiguiendo un fallo
  del entorno creyendo que era propio.
- **El mensaje de verdad solo sale mirando el error de UN test concreto**
  (`55000: no se puede conectar a la base de datos no válida`), nunca en el resumen de la
  corrida. Regla práctica: ante fallos en bloque con pila de Npgsql, **pedir el error de un
  test suelto antes de tocar nada**.
- Lo cura `PostgresFixture.TirarLaBaseSiQuedoInvalidaAsync`: mira el catálogo y la tira antes
  de empezar. `DROP DATABASE` **sí** funciona sobre una base inválida — es para lo que existe
  ese estado.

### 6.966.2c Cuánto cuesta desplegar, y dónde está la grasa

Medido sobre diez despliegues reales, no estimado:

| | tiempo |
|---|---|
| `Deploy a VPS` (total) | **~9 min**, muy estable (8m34s – 10m1s) |
| └ `verificar / build-and-test` | **~6 min** — es el CI otra vez |
| └ `publicar-y-desplegar` | **~3 min** — el despliegue de verdad |
| `CI` suelto (al hacer push) | ~5–6 min |
| Suite completa en local | ~6 min |

- ⚠️⚠️ **La misma suite corre TRES veces por despliegue**: en local, en el CI del push, y
  otra vez dentro del deploy (`verificar: uses: ./.github/workflows/ci.yml`). Las tres son
  deliberadas y ninguna sobra —local caza antes de subir, y la del deploy es la puerta que
  impide desplegar un commit roto—, pero **esperar a la del push cuando ya vas a desplegar es
  tiempo tirado**: el deploy la repite entera. **Regla: si el siguiente paso es desplegar, se
  lanza el deploy tras el push y no se espera al CI suelto.** Ahorra ~6 min por despliegue.
- **Durante la iteración, correr el ensamblado afectado, no la suite entera.** La completa se
  reserva para justo antes del push. Los `--filter` de un test concreto tardan 4 s frente a
  los 6 min del barrido.
- **La palanca que queda sin usar**: los 293 tests de integración van **en serie**, en una
  sola colección de xUnit, porque comparten una base. Son ~1,08 s por test con **dos
  `Respawn` cada uno** (§6.97.1 explica por qué se limpia también al terminar).
  Paralelizarlos exigiría una base por colección; bajaría los ~6 min a la mitad larga y
  afectaría a **las tres** ejecuciones. No está hecho: es un cambio de la infraestructura de
  tests con riesgo propio, y conviene decidirlo a la vista de esta tabla y no de una
  corazonada.

