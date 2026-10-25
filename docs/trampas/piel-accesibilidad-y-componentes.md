# La piel visual, accesibilidad, tests de front y componentes compartidos

> Parte de `CLAUDE.md` — extraído para no cargar todo el historial de trampas en cada sesión. Ver el índice en la raíz.

## 6.985 La piel: tipografía propia y tokens derivados del tema

`angular/src/styles/piel.scss` (SPA) y
`HttpApi.Host/wwwroot/Themes/LeptonXLite/Global/side-menu/css/font-bundle.css` (páginas MVC).
Antes la aplicación se veía como lo que era: Bootstrap por defecto dentro de LeptonX por
defecto.

- ⚠️⚠️ **REGLA: todo se deriva de las variables del tema, nunca de colores escritos a mano.**
  LeptonX tiene tres modos y **el acento lo elige cada inquilino en tiempo de ejecución**
  (§6.97). Una paleta fija rompería las dos cosas. Con `color-mix` sobre `--lpx-card-bg` y
  `--bs-body-color`, cada token apunta solo en la dirección correcta («algo más separado del
  fondo») y sale bien en claro y en oscuro sin declarar ninguno de los dos.
- ⚠️⚠️ **SON DOS MITADES Y HAY QUE ARREGLAR LAS DOS.** El SPA y las páginas MVC (login,
  segundo factor, activación) se sirven desde hosts distintos y **no comparten una sola hoja
  de estilos**. Arreglar solo el SPA deja la aplicación con dos tipografías: la propia en el
  panel y la de ABP justo en la primera pantalla que ve nadie. Se descubrió con el navegador,
  viendo qué peticiones salían.
- **La tipografía se sirve desde el propio dominio.** LeptonX carga un `font-bundle.css` que
  es una sola línea: un `@import` a `fonts.googleapis.com`. Es un viaje a un tercero
  **encadenado** —un `@import` no se descubre hasta que llega la hoja que lo contiene— con dos
  orígenes y su DNS y TLS cada uno. Los tres `.woff2` propios pesan **51 KB** en total.
  - En el SPA se sustituye apuntando la entrada `font-bundle` de `angular.json` a un archivo
    propio: **el tema carga ese bundle POR SU NOMBRE en tiempo de ejecución**, así que
    quitarlo de la lista no basta — pediría un archivo que no existe.
  - ⚠️ En las páginas MVC el original va **embebido en la DLL**. Se tapa con un archivo físico
    en `wwwroot` **en la misma ruta virtual**, que gana porque los estáticos se resuelven
    antes. **Comprobado pidiendo la URL**, no deducido. Si alguien mueve `UseStaticFiles`,
    esto deja de aplicarse en silencio y vuelve Inter desde Google.
- ⚠️ **TRAMPA — Bootstrap 5.3 no pinta el rayado de las tablas con `background-color`**, sino
  con `box-shadow: inset 0 0 0 9999px var(--bs-table-bg-state, var(--bs-table-bg-type, …))`.
  Poner `background-color: transparent` en la fila impar —que es lo primero que uno escribe—
  **no hace nada**. Hay que tocar `--bs-table-bg-type`. Se vio en una captura.
  - Y el realce al pasar por encima va en **`.table`**, no en `.table-hover`: ninguna tabla
    del proyecto lleva esa clase. Escrito como dice el manual de Bootstrap, la regla no se
    aplicaba en ningún sitio — y con el rayado ya apagado, las filas se quedaban sin ninguna
    forma de seguirlas con la vista, que es peor que no haber tocado nada.
- **Números tabulares** (`font-variant-numeric: tabular-nums`) en tablas y badges: media
  aplicación son columnas de recuentos y fechas, y con cifras de ancho variable no alinean.
- ⚠️⚠️ **LeptonX Lite tiene UNA SOLA combinación, y los «tres modos» no existen aquí.** Es
  armazón oscuro (`--bs-body-bg: #161616`) con contenido claro (`--lpx-card-bg: #ffffff`) — de
  ahí la trampa de §6.02.1. El bundle `bootstrap-dim` **se carga siempre**: está en la lista
  fija de estilos del paquete (`mergeStyles`), no es un tema alternativo. Las clases
  `.lpx-theme-light/dim/dark` existen en el CSS pero **nada en la versión Lite las aplica**:
  cambiar de tema es de LeptonX **Pro**, que §8 prohíbe.
  - Por tanto el bloque `.lpx-theme-dark/.lpx-theme-dim` de `piel.scss` **hoy no se ejecuta**.
    Se deja como seguro barato, pero no está verificado y no puede estarlo.
  - ⚠️ Y para cuando alguien lo intente: **cambiar la clase `.lpx-theme-*` a mano no vale como
    prueba**. Las variables del tema no dependen de esa clase, así que no cambian, y la
    medición sale falsa — parece que los tokens están vacíos o congelados. Pasó al verificar
    esto la primera vez.

### 6.985.05 Accesibilidad: de 91 fallos a cero

Auditado con **axe-core** sobre las once pantallas, ya autenticado. El resultado inicial fue
mejor de lo esperado en lo estructural —**ni una etiqueta ausente, ni un nombre accesible que
falte, ni ARIA rota**— y peor en lo visual: **91 elementos incumplían contraste (WCAG AA), en
las once pantallas**.

Eran **cuatro causas**, no 91 problemas:

- ⚠️⚠️ **El gris del texto secundario, y de lejos el mayor.** 45 sitios usan
  `--bs-secondary-color` y 37 lo hacen con el fallback `#6c757d`, que sí cumpliría… pero **el
  fallback nunca entra**, porque LeptonX define la variable en `#8e9092`: **3,20** sobre
  blanco y **2,94** sobre el fondo claro. Redefinirla a `#666d75` (5,24 y 4,81) arregló la
  mayoría de un plumazo. El valor salió de **calcular** los ratios: el propio `#6c757d` de
  Bootstrap se queda en 4,31 sobre el fondo claro y tampoco pasaba.
- ⚠️ **`opacity` mata el contraste sin dejar rastro en ningún color.** Pasó dos veces:
  `.id-corto` en `/metrics` (0,75) y el copyright del pie (0,8). En el segundo, el color
  calculado ya era el correcto y **el fallo no se movió ni un elemento** — porque lo que se ve
  no es ningún color escrito en el CSS. Un color secundario declarado se puede revisar; una
  opacidad hay que renderizarla para saber qué da.
- ⚠️ **Los colores semánticos de LeptonX son demasiado claros para llevar texto blanco**:
  `bg-success` daba 2,34 y `bg-warning` 2,05, en los badges de estado que son de lo más leído
  de `/jobs`. Se oscurecen con `color-mix`, con el porcentaje calculado para cada uno.
- ⚠️⚠️ **EL ACENTO DEL INQUILINO NO SE PUEDE ARREGLAR CON UN COLOR FIJO.** Un botón primario
  es el color del cliente con texto dentro, y con el de producción (`#f72585`) **ni el blanco
  (3,78) ni el texto oscuro (4,08) llegaban a 4,5**. Como el color lo elige otra persona, la
  solución tiene que ser código: `contraste.ts` calcula dos variantes y `branding.provider.ts`
  las publica como variables CSS.
  - `relleno()` → `--app-acento-solido` + `--app-sobre-acento`, para superficies **rellenas**
    (botones, badges). Si el acento ya vale, **no se toca**; si no, se oscurece **lo mínimo**.
  - `acentoParaTexto()` → `--app-acento-texto`, el problema simétrico: el acento **como color
    de texto** sobre el fondo claro (enlaces «ver todo», el nombre del usuario en la portada).
  - **El acento crudo se sigue usando donde no carga texto**: bordes, aro de foco, barra
    lateral. La marca se ve igual donde se puede ver.
  - Tiene tests (`contraste.spec.ts`), incluido un barrido de **216 colores** que exige que
    para *cualquiera* el resultado sea legible. Los casos pensados cubren lo que se pensó; ese
    cubre lo que no.

**Lo que axe no mide, y hubo que recorrer con el teclado:**

- El foco se ve en **todas** las paradas (lo puso la piel con `:focus-visible`), no hay
  trampas y el orden es lógico. Eso estaba bien.
- Pero eran **8 paradas atravesando el menú antes de llegar al contenido, en cada
  navegación**. De ahí `SaltoAlContenidoComponent` (criterio 2.4.1 de WCAG). ⚠️ Se esconde
  con `transform`, **nunca** con `display:none` ni `visibility:hidden`: esas dos lo sacarían
  del orden de tabulación y no habría nada que enfocar.

⚠️⚠️ **La trampa del método, y costó tres auditorías.** El script daba **exactamente el mismo
informe** una y otra vez —18 fallos, mismo desglose— aunque los arreglos estaban verificados
uno a uno en el navegador. La causa: `entrar()` esperaba con `waitForTimeout(9000)`, la URL ya
era la del SPA pero **el armazón aún no estaba montado**, así que axe medía la portada anónima
once veces. Un informe estable parecía una señal de que los arreglos no servían, y era lo
contrario. **Regla: esperar al elemento (`waitForSelector('.lpx-topbar')`), y que el script
ABORTE si no está.** La confirmación explícita es lo que lo destapó.

### 6.985.1 Tests del front: hay, pero no con `ng test`

`angular/vitest.config.mts` + `yarn test:unit`, enganchado al CI. 28 tests.

- ⚠️⚠️ **`ng test` NO FUNCIONA EN ESTE PROYECTO Y NO ES CONFIGURACIÓN.** El arrancador de
  Angular emite `await import('zone.js/testing')` **en el nivel superior** del módulo; al
  detectar zone.js, Angular le pide a esbuild que degrade `async/await`
  (`'async-await': nativeAsyncAwait` en `@angular/build/src/tools/esbuild/utils.js`, con el
  comentario *«Native async/await is not supported with Zone.js»*), y un `await` de nivel
  superior **no se puede degradar**. El build de test falla entero antes de ejecutar una sola
  prueba. Esta aplicación usa zone.js por diseño (§5.1). Se llegó a esto leyendo el código de
  Angular.
- ⚠️ Antes de eso hacía falta un **`.browserslistrc`**: sin él, browserslist cae a su consulta
  por defecto y arrastra `and_qq` y `and_uc`, dos navegadores que tampoco soportan top-level
  await. El error habla del «entorno destino» y no menciona browserslist ni el archivo que
  falta.
- **Qué se prueba, y por qué solo eso.** Los fallos de front de este proyecto —callbacks fuera
  de la zona, variables CSS del tema, tablas que se salen en móvil— **no los caza ningún test
  unitario**: viven en un navegador y se han encontrado automatizándolo. Lo que sí se rompe en
  silencio son las **funciones puras escritas dos veces**, en TypeScript y en C#: la forma de
  las URLs de los espacios (§6.965) y la del logo (§6.97). Ahí un carácter deja a un inquilino
  sin poder iniciar sesión, y hasta ahora **solo la mitad de C# tenía tests**.
  - Los casos son **los mismos** que los de `RaizDelEspacioTests`, a propósito: dos listas
    idénticas en dos lenguajes es lo único que detecta que se han separado. Comprobado que
    siete se ponen rojos al añadir una barra final.
  - Se encontró de paso una divergencia real: C# hacía `TrimEnd('/')` sobre el origen y el
    TypeScript no. No llegaba a morder porque `location.origin` nunca trae barra final, pero
    «iguales salvo por una entrada que hoy no ocurre» no es lo mismo que iguales. Ya lo hace.

## 6.99 Componentes compartidos de interfaz y búsqueda global

`angular/src/app/shared/`. Nacieron de un problema concreto: las once pantallas escribían
**cada una por su cuenta** el mismo `<h2 class="titulo">`, el mismo `<p class="explicacion">` y
el mismo «Cargando…», con la `.explicacion` repetida en diez hojas de estilo casi idénticas.
Eso no era solo duplicación — era la razón práctica de que la aplicación se viera como once
pantallas sueltas: cambiar el espaciado obligaba a tocar once archivos, así que no se cambiaba.

- **`app-cabecera`** — título, descripción y hueco para acciones. Las acciones van por
  **proyección de contenido**, no por `@Input`: son botones con su lógica, sus permisos y sus
  estados de carga; pasarlos como datos habría convertido esto en un mini-framework de botones.
  - ⚠️ **Dos de las once descripciones llevan formato** (`<b>` y varias claves de traducción
    dentro), y eso no cabe en un `@Input` de texto. Hay un segundo hueco,
    `<ng-content select="[descripcion]">`, para ese caso.
- **`app-esqueleto`** — barras grises en vez de la palabra «Cargando…», que estaba en doce
  sitios. No es cosmética: varias pantallas **sondean cada minuto**, así que un mensaje que
  aparece y desaparece parpadea y hace saltar el contenido. ⚠️ Solo debe pintarse en la
  **primera** carga (`*ngIf="cargando && !hayDatos"`); si no, la tabla se convierte en barras
  grises cada sesenta segundos.
- **`app-vacio`** — admite una **pista** además del texto, y por una razón: «no hay trabajos»
  no distingue «todo va bien» de «el worker no está corriendo», que es justo la pregunta que
  trae a alguien a abrir esa pantalla (§6.0.1).

### 6.99.1 Paleta de comandos (Ctrl+K)

`app-buscador`, montado en `AppComponent` **fuera del router** para que el atajo funcione en
cualquier pantalla.

- **Dos fuentes con ritmos distintos**: las pantallas están escritas en el cliente y se filtran
  a cada tecla sin red de por medio; los datos (inquilinos, usuarios) van con `debounceTime`.
  Así la lista responde aunque la red vaya lenta.
- ⚠️ **Cada fuente se pide SOLO si su permiso está concedido**, en vez de pedirla y tragarse el
  403. Una paleta se usa muchas veces al día: sin ese filtro, un usuario sin esos permisos
  generaría tres peticiones fallidas por búsqueda — ruido en la auditoría y, peor, en la
  proporción de fallos por IP, que es la señal con la que el bloqueo automático decide a quién
  vetar (§6.15). Es justo lo que no conviene que parezca un escaneo.
- ⚠️ **El atajo no se roba a quien está escribiendo** en un `input` o `textarea`: Ctrl+K es
  también «borrar hasta el final de línea» en algunos entornos.
- Los fallos de cada fuente se tragan por separado: si el listado de usuarios falla, la paleta
  sigue enseñando las pantallas. Un buscador en blanco por una fuente con un mal momento es
  peor que uno incompleto.
- ⚠️ **`align-items: flex-start` en el fondo**, no el `stretch` por defecto de flex: sin eso el
  panel se estira hasta el borde inferior y con tres resultados queda un rectángulo enorme
  medio vacío. Se vio en una captura, no leyendo el CSS.

### 6.99.2 ⚠️ Comillas invertidas dentro de `styles: [...]`

Un comentario CSS con una palabra entre comillas invertidas **cierra el template literal a
mitad**. El error que sale es `FatalDiagnosticError: Failed to resolve styles at position 1 to
a string` y **no menciona el archivo**; el dev server se queda sin arrancar el puerto, lo que
desde fuera parece «sirve CSS viejo» y manda a buscar en la dirección equivocada. Pasó
escribiendo `` `stretch` `` en un comentario de `buscador.component.ts`.

#### 6.966.096 Feature y lado son ejes distintos (segunda vez que muerde)

§6.966 lo dice de los permisos y las features; esto es la misma lección un nivel más abajo,
con los permisos de un módulo. Al atar el catálogo a su feature se le puso además
`MultiTenancySides.Tenant` «por prudencia», sumando las dos restricciones.

- ⚠️⚠️ **Con eso el host no podía tener el catálogo NI CONTRATÁNDOLO.** El permiso no existe
  para él, así que la pantalla desaparece de su menú y no hay forma de recuperarla desde
  ninguna parte. Nada falla, ningún test se pone rojo: el módulo simplemente no está.
- La regla: **la feature dice quién lo tiene contratado; el lado dice de quién es la cosa.**
  Duplicar la restricción en el segundo eje no protege nada y cierra una puerta que después
  no se puede abrir.
- **La invariante que sí vale es la coherencia entre los dos**, y así está escrito el barrido:
  si la feature no se le ofrece al host (`isAvailableToHost: false`), sus permisos van
  `Tenant` —serían permisos sobre algo que no puede contratar jamás—; si se le ofrece, van
  `Both`. Escrito como «siempre Tenant», el guardián prohibía el caso correcto.
- ⚠️ **Y el barrido saltaba el permiso PADRE.** Los prefijos llevan punto final
  (`IgasConsole.Products.`) y el padre se llama `IgasConsole.Products`, sin él: un `StartsWith` a
  secas revisa los hijos y **no mira al padre**. Se vio saboteando —quitándole el
  `RequireFeatures` al padre— y comprobando que el test pasaba tan tranquilo, que es la peor
  forma de fallar que tiene un guardián. **Sabotear en los dos sentidos es lo que lo destapó;
  verlo en verde no demuestra nada.**
- Todo esto se encontró **en el navegador**, no en los tests: los de integración llaman al
  application service directamente y no pasan por la comprobación de permisos que sí hace el
  menú del SPA. Es la misma clase de punto ciego que §6.15.1 (el DTO con `Action`).

#### 6.966.097 Los módulos de la propia instalación (`/tenants`)

Botón «Módulos de la instalación», junto al alta de inquilinos, bajo el permiso de ABP
`FeatureManagement.ManageHostFeatures`. Sin él, una feature con `isAvailableToHost: true`
nace apagada y **no hay forma de encenderla desde la interfaz**.

- Es el **mismo componente de ABP y el mismo proveedor** que el de los inquilinos (`"T"`); lo
  único que cambia es que la clave va **vacía**. Así lo entiende el servidor:
  `FeatureAppService.CheckProviderPolicy` trata «proveedor T + tenant nulo + clave nula» como
  el host y lo exige bajo `ManageHostFeatures` (verificado en `refs/abp`). No hizo falta nada
  propio.
- Es **aquí donde `isAvailableToHost` hace su único trabajo real**: `FeatureAppService` lo usa
  para no listar en esta pantalla los módulos que no son de la instalación. Comprobado en el
  navegador — sale el catálogo y sus cuotas, y el restaurante no.
- ⚠️ Al verificarlo, **borrar la fila de `AbpFeatureValues` a mano no apaga nada**: ABP cachea
  los valores y un `DELETE` directo se salta la invalidación, así que la pantalla sigue
  apareciendo y parece que la feature no gobierna. Hay que apagarlo por la pantalla o
  reiniciar. Comprobado con las dos mitades: apagada no está en el menú, contratada aparece y
  la pantalla responde.

### 6.99.3 Pantallas por tema, y el techo de las listas

Medido con un script que recorre el DOM (`scratchpad/pw/ux.js`): alto de scroll, bloques que
compiten por la atención, tablas, controles visibles y llamadas distintas a la API. La
impresión de «esto está saturado» era correcta y tenía número: **`/metrics` medía 11.297 px
—12,6 pantallas— con 3 tablas, 37 controles y 11 llamadas al abrirla.**

- **Juntaba cuatro cosas que no se parecen**: gráficos del servidor, tráfico bloqueado,
  auditoría y el formulario del bot de Telegram. Se reparte por la pregunta que trae a
  alguien a la pantalla, no por el permiso: `/metrics` (¿cómo está el servidor?), `/audit`
  (¿quién tocó esto?) y `/alertas` (¿a dónde van los avisos?). El tráfico bloqueado se va a
  `/security`, que es donde se decide bloquear.
  - **Ningún permiso nuevo**: cada trozo conserva el que ya tenía. Un permiso paralelo daría
    dos respuestas a la misma pregunta.
  - El atajo que antes filtraba en sitio ahora **navega**: la IP de una fila de tráfico
    bloqueado lleva a `/audit?ip=…`, que es la pregunta siguiente.
  - ⚠️ Al partirla hay que **quitar también las cargas que se quedan huérfanas**. `/metrics`
    seguía pidiendo ajustes, cambios, peticiones y bloqueados para pintar secciones que ya no
    existían: 11 llamadas para cinco gráficos, y ni un error.
- ⚠️⚠️ **Después de partirla, el problema que quedaba no era el diseño: eran las listas sin
  techo.** `/sessions` medía 9.632 px con **144 controles** y `/audit` 9.764 px. Y no es
  artificio de las pruebas — se crea **una sesión por login** y la purga es a 30 días, así
  que quien entra a diario desde tres dispositivos acumula decenas. Una lista así deja de
  servir para lo único que se le pide: reconocer de un vistazo lo que **no** deberías ver
  ahí.
  - Las dos se paginan **en el cliente**, y ahí sí hay criterio: son datos que **ya vienen en
    la respuesta** (las sesiones de una persona; las ≤200 entradas que devuelve el registro,
    ya filtradas por el servidor). Paginar en el servidor sería una llamada por página para
    recortar algo que se tiene delante. Lo contrario vale para el historial de cambios, que
    ya paginaba en el servidor porque la tabla es grande y el filtro es suyo.
  - ⚠️ **Cualquier cambio de filtro vuelve a la página 0.** Quedarse en la página 5 de un
    filtro nuevo enseña una lista vacía que se lee como «no hay resultados».
  - Los controles solo se pintan si hay **más de una página**: unos botones de paginado sobre
    seis filas son ruido.
- **Resultado**: ninguna pantalla pasa de **2,4 pantallas de scroll**; `/metrics` bajó a 1,4
  y `/sessions` a 1,6.
- **Regla**: una pantalla nueva con una tabla sin tope no está terminada. Y la forma de
  saberlo es **medirla**, no mirarla — en un entorno de trabajo con cuatro filas todas se ven
  bien.

### 6.99.4 «Mi cuenta»: una portada, no cinco entradas sueltas

El grupo tenía cinco hermanas en el menú —foto, Telegram, sesiones, avisos, claves— y cada
una abría **una sola tarjeta en medio de una pantalla en blanco**. En ningún sitio se decía
quién eras, desde cuándo, con qué roles ni si tu cuenta estaba protegida. Descrito por quien
lo usa: «se ve vacío y poco profesional, y no contamos con información del usuario».

**El patrón** que siguen GitHub, Google, Stripe y Atlassian es el mismo en las cuatro: una
página de cuenta que empieza **identificando a la persona** (avatar, nombre, usuario, correo,
roles, antigüedad) y agrupa el resto por **áreas** — seguridad, personal, conexiones—. Nunca
entradas hermanas en la navegación principal, porque desde fuera no se ve qué las une ni cuál
abrir primero. `/mi-cuenta` copia esa forma; **las pantallas de detalle no se han tocado**.

- ⚠️ **Cada tarjeta enseña su ESTADO, no solo su nombre.** «Segundo factor: desactivado» en
  ámbar contesta desde la portada lo que «Segundo factor» a secas obliga a entrar a averiguar.
  Es la diferencia entre un índice y un panel, y es la razón de que exista `MiCuentaAppService`:
  la pantalla necesita **los cinco recuentos a la vez**, y pedirlos por separado serían cinco
  llamadas para pintar una cabecera (§6.99.3).
- ⚠️ **La identidad NO se pide a la API.** Nombre, usuario, correo y roles ya viajan en
  `/api/abp/application-configuration` (`currentUser`), que el SPA descarga al arrancar.
  Repetirlos daría **dos fuentes para el mismo dato** y un instante en que la pantalla se
  contradice. El DTO trae solo lo que ABP no cuenta.
- ⚠️⚠️ **«Acceso anterior» es el PENÚLTIMO inicio de sesión, no el último.** Cuando esta
  pantalla se abre, la entrada de la sesión en curso **ya está escrita**: quedarse con la más
  reciente enseñaría «hace unos segundos», el propio reflejo de quien mira. La pregunta que el
  dato contesta es «la vez anterior, ¿fui yo?». Se piden dos filas y se devuelve la segunda.
  Tiene tres tests y se comprobó que se ponen rojos al cambiar `ultimos[1]` por `ultimos[0]`.
  - Y se filtra por **acción exacta** (`LoginSucceeded`): con los fallidos contando, el
    «anterior» sería el intento de alguien que **no** entró.

**Tres trampas del menú, todas silenciosas:**

- ⚠️ **LeptonX ignora el `path` de un padre CON hijos**: lo trata como desplegable y la URL no
  cambia. Comprobado en el navegador.
- ⚠️ **Declarar la MISMA ruta en el padre y en un hijo hace que el hijo no se pinte.** También
  comprobado: la entrada no salía y no fallaba nada. El grupo se queda sin ruta y la portada
  entra como su primera entrada (que es además lo que hace GitHub).
- ⚠️⚠️ **Una entrada sin `requiredPolicy` se registra `invisible` y solo la enciende su
  `routes.patch(...)`** en la suscripción a `currentUser`. A la portada se le olvidó el suyo y
  **simplemente no aparecía** — sin error, sin traza y con su clave de traducción resolviendo
  perfectamente. Se vio listando el menú desde el navegador. **Regla: entrada nueva bajo «Mi
  cuenta» sin permiso propio ⇒ añadir su línea a esa lista.**

**De paso**: el botón de la foto de perfil estaba en **español fijo** (`'Elegir foto'`) y salía
tal cual dentro de la interfaz en inglés. Era un ternario de plantilla, que no admite el pipe
(§6.95), así que se resuelve en el componente con `instant(...)`.

