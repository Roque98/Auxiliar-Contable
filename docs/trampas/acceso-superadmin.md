# Esconder la puerta del superadministrador

> Parte de `CLAUDE.md` — extraído para no cargar todo el historial de trampas en cada sesión. Ver el índice en la raíz.

## 6.98 Esconder la puerta del superadministrador

`Acceso:SegmentoDelHost` mueve **a la vez** el panel del host y el formulario de login fuera de
sus dos rutas por defecto. Los espacios de inquilino (`/app/{nombre}`) no se tocan: son
direcciones que sus usuarios tienen que poder recibir y escribir.

```
Acceso__SegmentoDelHost=g7k2p

  https://sitio/                  ->  «este espacio no está disponible»
  https://sitio/g7k2p             ->  panel del host
  https://api.sitio/Account/Login ->  404
  https://api.sitio/g7k2p         ->  formulario de login
```

- ⚠️⚠️ **ESTO REDUCE RUIDO, NO ES UNA CERRADURA.** Frena el escaneo automático —que es la
  inmensa mayoría del tráfico hostil que registra esta instalación— y evita que quien pase por
  el dominio se encuentre un panel de administración anunciándose. **No** frena a quien vaya a
  por ti: el segmento se adivina a fuerza de intentos, y quien construya un
  `/connect/authorize` válido (los parámetros están en el JavaScript del SPA, que es público)
  recibe la redirección al formulario y con ella el segmento. Lo que protege esa cuenta es el
  **segundo factor**. Escribirlo aquí para que nadie confunda una cosa con la otra.
- **Vacío = comportamiento por defecto**, y ese es el **interruptor de emergencia**: se borra
  la variable de `/etc/auxiliar-contable/env`, se reinicia el servicio y la instalación vuelve a
  como estaba, sin desplegar nada. Development lo deja vacío a propósito.
- ⚠️ **El segmento NO está en el bundle de Angular, y es todo el asunto.** El JavaScript se
  sirve público —lo necesitan los espacios de inquilino—, así que un segmento compilado dentro
  se lo baja cualquiera y esconder la ruta no habría servido de nada. Vive **solo** en la
  configuración del servidor, y el SPA lo *confirma* preguntando a `/api/acceso/espacio/{s}`
  antes de arrancar (mismo momento que la resolución de inquilino, ver §6.965).
  - Ese endpoint es un oráculo, sí. Pero preguntar ahí **no es más barato** que probar la URL
    y sale **más caro**: contesta **404** al fallar —no un 200 con `{"esHost":false}`, que fue
    lo primero que se escribió— y así cada intento cuenta como fallo para el detector de
    sondeo de rutas del bloqueo automático (§6.15), que se dispara con 40 fallos y ≥80 % de
    error en 10 min. Con el 200 el barrido le habría parecido tráfico sano.
- ⚠️⚠️ **Sin el guardián de `OnRedirectToLogin`, el segmento se regala.** La redirección al
  login lleva la ruta nueva en la cabecera `Location`, así que **cualquier** página MVC
  protegida —`/Account/Manage` es la obvia— se convierte en un oráculo gratis: se pide sin
  sesión y ahí está el segmento, sin adivinar nada. Se responde 404 salvo que la petición
  venga de `/connect/authorize`, que es el único camino legítimo hasta el formulario.
- ⚠️ **Mover el login le quitaba su límite de intentos.** `EsRutaDeCredenciales` tenía
  `/Account/Login` escrito a mano: sin añadir el segmento, el formulario de contraseñas pasa
  de 10 intentos por minuto a los 120 de la navegación normal. No falla nada visible —
  simplemente se pueden probar doce veces más contraseñas, que es lo contrario de lo que
  persigue esconder la ruta.
- ⚠️ **La `redirect_uri` del host se mueve con el segmento.** El panel ya no está en la raíz,
  así que `SincronizadorDeRedirecciones` registra `{raiz}/{segmento}`. Sin eso, el **único**
  que no podría entrar sería justo el superadministrador, y con un `invalid_request` que ocurre
  en el servidor de autenticación, donde no se ve desde el navegador. Comprobado de las dos
  formas: con la raíz pelada da 400, con el segmento da 302 al login.
  - Es el mismo acoplamiento C#/TypeScript de §6.965 (`AccesoConsts.RaizDelHost` ↔
    `raizDelHost`), y lo fija `AccesoConstsTests`.
- ⚠️ **El router de Angular sigue corriendo aunque `AppComponent` corte el armazón.** Los
  guardias se ejecutan durante la navegación, haya o no un `router-outlet` en pantalla, así
  que una URL inexistente que coincidiera con una ruta protegida —`/metrics` sin el segmento,
  el caso obvio— disparaba `authGuard` y se llevaba el navegador a `/connect/authorize`: en
  vez de la pantalla neutra salía **la de login**, justo la pista que todo esto existe para no
  dar. Se arregla poniendo la ruta actual entera como `APP_BASE_HREF`, con lo que el router ve
  la cadena vacía y cae en `path: ''`, que no tiene guardia.
  - Se encontró **en el navegador**. Desde el servidor todo se veía perfecto.
- ⚠️ **`deploy.yml` comprobaba el 429 curleando `/Account/Login`.** Con el acceso oculto esa
  ruta da 404, y el paso habría seguido «pasando» —un 404 no es 429 ni un redirect, así que
  termina en su `::warning::`— sin comprobar ya nada. Apunta a `/connect/token`, que no se
  mueve, comparte la partición estricta del limitador y no obliga a meter el secreto en el
  workflow.
- **nginx no necesitó cambios**: su `try_files $uri $uri/ /index.html` ya devuelve el SPA para
  cualquier ruta, y el segmento lo resuelve la aplicación.

### 6.98.2 ⚠️⚠️ El login no era la única puerta

Esconder `/Account/Login` y dejar las otras dos abiertas no esconde nada. Se vio **mirando la
pantalla de login ya desplegada**: enseñaba «Register» y «Forgot password?», y las dos páginas
respondían **200 en su dirección por defecto** mientras el login llevaba días escondido.
`/Account/Register` identifica un ABP igual de bien que `/Account/Login`.

- **`RutaDeLoginConvention` mueve las TRES**: el login al segmento pelado, y `Register` y
  `ForgotPassword` a `/{segmento}/registro` y `/{segmento}/recuperar`. Los enlaces internos las
  siguen solos porque usan `Url.Page`/`asp-page`, que resuelven por **nombre** de página.
  - El límite estricto de intentos las cubre sin tocar nada: `EsRutaDeCredenciales` ya devuelve
    true para todo lo que cuelga del segmento.
- ⚠️⚠️ **El auto-registro venía ABIERTO, y no lo abrió nadie.**
  `AccountSettingDefinitionProvider` define `IsSelfRegistrationEnabled` con **`"true"`**
  (verificado en `refs/abp`), así que en producción cualquiera podía crearse una cuenta con
  sesión válida en el host. No es una escalada —una cuenta nueva no recibe permisos— pero
  contradice todo §6.98: se escondía la puerta principal y quedaba abierta la de servicio.
  Se cierra con `CierreDelAutoRegistroSettingProvider`.
  - **Efecto secundario deseable**: `Login.cshtml` pinta el enlace «Register» **solo si** ese
    ajuste está en true, así que el enlace desaparece solo, sin tocar la vista (regla de §5.2).
  - ⚠️⚠️ **LA CAPA IMPORTA, y costó una vuelta.** Los proveedores de definiciones corren **en
    orden de registro** y el último que llama a `context.Add` con el mismo nombre **reemplaza**
    la definición. Puesto en `Application.Contracts`, el proveedor propio corría **antes** que
    el de ABP, que volvía a poner `"true"` encima: **el ajuste seguía abierto y no fallaba
    nada**. Vive en `Application`, que carga después de `Volo.Abp.Account.Application`.
    - Regla general: **para pisar un ajuste de un módulo de ABP, el proveedor propio tiene que
      estar en una capa que cargue DESPUÉS de ese módulo.**
    - Solo se ve **comprobando el ajuste contra `/api/abp/application-configuration`**. En el
      código las dos versiones se leen igual de bien.
- **El enlace «Forgot password?» se esconde con CSS** (`global-styles.css`), porque
  `Login.cshtml` lo pinta **sin condición**. No es cosmética: `IEmailSender` es
  `NullEmailSender`, así que esa página acepta el correo, dice que ha enviado un mensaje y **no
  envía nada**. Quien lo use espera indefinidamente algo que no existe — peor que no ofrecerlo.
  Vuelve el día que haya correo de verdad (ver `BACKLOG.md`).
- ⚠️ La pantalla neutra de «este espacio no está disponible» se pinta **fuera del armazón de
  LeptonX**, así que cae sobre `--bs-body-bg` — el gris casi negro del armazón, no una
  superficie de contenido (§6.02.1). Estaba con el texto en gris oscuro: **ilegible**. Ahora
  pinta su propia ficha. Se vio en una captura de producción; en un componente aislado uno da
  por hecho que el fondo es claro.

### 6.98.1 ⚠️ Lo que corre antes de arrancar Angular es pantalla en blanco

Resolver el espacio ocurre **antes de `bootstrapApplication`** (§6.965 explica por qué tiene
que ser ahí), así que **cada petición de ese trozo se paga en blanco**. Es fácil olvidarlo
porque en local, contra `localhost`, no se nota nada.

- La primera versión preguntaba **dos cosas por separado** —«¿está el panel en la raíz?» y
  luego «¿es este el segmento?»— en **dos viajes seguidos**. Medido contra producción: ~0,4 s
  con buena red, el doble desde un móvil. El usuario lo describió como «se queda en blanco un
  rato al actualizar», que es exactamente lo que era. El servidor sabe las dos cosas, así que
  ahora las contesta juntas y devuelve el prefijo: **una sola llamada**, lo mismo que ya
  pagaban los espacios de inquilino.
- Y `index.html` era literalmente `<app-root></app-root>`: durante todo ese rato no había
  **nada** que pintar. Lleva un spinner **en línea** —tiene que serlo, porque lo que se cubre
  es el momento en que aún no ha llegado ninguna hoja de estilos— dentro de `<app-root>`, así
  que Angular lo borra solo al pintar. Medido: algo en pantalla a los ~60-120 ms en vez de
  cerca de un segundo de blanco.
- **Regla para más adelante: nada nuevo entra en `resolver-espacio.ts` sin contar los viajes.**
  Cualquier pregunta que se añada ahí sale directamente del tiempo que el usuario mira una
  pantalla vacía.

