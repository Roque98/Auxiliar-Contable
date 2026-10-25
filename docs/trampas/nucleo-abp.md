# Magia invisible de ABP, Angular fuera de zona y 2FA por Telegram

> Parte de `CLAUDE.md` — extraído para no cargar todo el historial de trampas en cada sesión. Ver el índice en la raíz.

## 5. Magia invisible de ABP (lo que NO se ve leyendo el código)

Verificado en `refs/abp`:

- **Transaccionalidad por defecto**: cada método de un application service se ejecuta dentro de un Unit of Work ambiental que **abre y commitea una transacción automáticamente**. No abras transacciones a mano. `SqlQueryRaw` sobre el `DbContext` del UoW ve las escrituras previas del mismo UoW.
- **Filtro de tenant automático (EF)**: las queries LINQ filtran por `TenantId` del tenant actual vía *global query filters*. **No** filtres por `TenantId` en LINQ. `ICurrentTenant.Id` da el tenant actual.
  - ⚠️⚠️ **TRAMPA GRANDE — una entidad SIN `DbSet` no recibe los filtros globales.** ABP los
    aplica recorriendo `modelBuilder.Model.GetEntityTypes()` **dentro de
    `base.OnModelCreating()`**, que en `AuxiliarContableDbContext` es la **primera línea**. EF ya
    conoce ahí las entidades que tienen una propiedad `DbSet<>`; una registrada solo más
    abajo con `builder.Entity<X>()` llega tarde y **se queda sin filtro de tenant y sin el
    de soft delete**. No falla nada al compilar y las consultas devuelven las filas de
    todos los tenants.
    - Pasó de verdad con `Notificacion`. Todas las entidades del repo tenían `DbSet` por
      costumbre, así que el problema no apareció hasta la primera que no lo tenía.
    - Lo vigila un test de arquitectura
      (`Toda_entidad_con_filtro_global_esta_declarada_como_DbSet`), comprobado que se pone
      rojo al quitar el `DbSet`. **Regla: toda entidad propia va también como `DbSet`.**
  - El SQL crudo es otra historia y está más abajo: ahí el filtro se escribe a mano.
    `Products/Sql/*.sql` lo llevan, y hay tests de aislamiento que lo vigilan — sin ellos,
    borrar esa línea no rompería nada visible.
- **Auditoría automática**: `CreationTime`, `CreatorId`, `LastModificationTime`, `LastModifierId`, `DeletionTime`, `DeleterId` los llena ABP. No los asignes.
- **Soft delete**: las entidades `ISoftDelete`/`FullAudited*` no se borran; se marca `IsDeleted = true` y un *global filter* las oculta en LINQ.
  - ⚠️ **TRAMPA**: el SQL crudo (`SqlQueryRaw` **y** Dapper) **NO** pasa por los filtros globales, aunque `SqlQueryRaw` corra "sobre EF Core". En cada `.sql` de la base principal agrega **a mano** `WHERE "IsDeleted" = false` y el filtro de `TenantId`. Ver `Products/Sql/GetProductSalesReport.sql`.
- ⚠️ **Identificadores en PostgreSQL**: sin comillas, PostgreSQL **pliega los identificadores a minúsculas**. EF Core crea las tablas y columnas en PascalCase (`"AppProducts"`, `"IsDeleted"`), así que **todo `.sql` de la base principal debe citar cada identificador con comillas dobles** — alias incluidos, para que el materializador de EF encuentre las propiedades del DTO. `SELECT * FROM AppProducts` falla; `SELECT * FROM "AppProducts"` funciona.
  - Otras diferencias frente a T-SQL que ya están aplicadas en los `.sql` del repo: `TOP (n)` → `LIMIT n`, `COUNT_BIG(*)` → `COUNT(*)`, `bit`/`CAST(0 AS bit)` → `boolean`/`false`, `OFFSET … FETCH NEXT` → `LIMIT … OFFSET`, y la igualdad null-safe `IS NOT DISTINCT FROM` en vez de `(x IS NULL AND y IS NULL) OR x = y`.
  - Un parámetro que puede viajar `NULL` necesita tipo explícito (`CAST({1} AS uuid)`): Npgsql no puede inferirlo del valor.
- **Fechas con Npgsql**: EF Core mapea `DateTime` a `timestamp with time zone`. Npgsql **exige `DateTimeKind.Utc`** para ese tipo y `Unspecified` para `timestamp without time zone`; pasar el Kind equivocado lanza en tiempo de ejecución, no compila mal.
- **Registro por convención (DI)**: implementar `ITransientDependency`/`IScopedDependency`/`ISingletonDependency` (o ser un application service/repositorio) registra el servicio automáticamente. No hace falta registrarlo a mano.
- ⚠️ **TRAMPA — `UseErrorPage()` convierte tus 4xx/5xx en redirects, y solo fuera de Development**: el Host llama a `app.UseErrorPage()` dentro de un `if (!env.IsDevelopment())`, y eso es `UseStatusCodePagesWithRedirects("~/Error?httpStatusCode={0}")` (verificado en `refs/abp`, `AbpApplicationBuilderErrorPageExtensions`). Ese middleware envuelve a todo lo que se registre después y **transforma cualquier respuesta 400-599 SIN CUERPO en un 302** a la página de error.
  - Consecuencia: un middleware que devuelve un código de estado a secas funciona en tu máquina y se rompe en producción. Pasó de verdad con el rate limiting: el 429 llegaba como `302 /Error?httpStatusCode=429`, los clientes de API recibían un redirect sin sentido y el `access.log` de nginx registraba 302, con lo que la jail de fail2ban que busca 429 no habría baneado nunca a nadie.
  - Solución: **escribe cuerpo y `ContentType`**. `StatusCodePages` se abstiene si la respuesta ya los trae. Ver `HttpApi.Host/Monitoring/RateLimitingServiceCollectionExtensions.cs`.
  - Corolario para las pruebas: los tests de integración y el `dotnet run` local corren en Development, donde este middleware **no** existe. Una respuesta HTTP que dependa del código de estado hay que verificarla contra el entorno real.
    - ⚠️ Pero **sí se puede probar en local**, y es la forma de no descubrir esto desplegando:
      basta arrancar el Host con `ASPNETCORE_ENVIRONMENT=Staging`. Como el user-secrets solo
      se carga en Development, hay que pasarle a mano `ConnectionStrings__Default`,
      `AuthServer__CertificatePassPhrase` y `StringEncryption__DefaultPassPhrase` (se sacan de
      `dotnet user-secrets list`), y `Acceso__SegmentoDelHost` si se quiere probar con el
      acceso oculto.
  - ⚠️⚠️ **La trampa está ARREGLADA DE RAÍZ desde `CuerpoEnLosErroresMiddleware`** (§5.0.1),
    así que ya no hace falta acordarse en cada sitio. Lo de arriba se conserva porque explica
    **por qué** existe ese middleware y qué pasa si alguien lo quita.

#### 5.0.1 El arreglo de raíz: `CuerpoEnLosErroresMiddleware`

Los cuatro parches anteriores (429 del rate limiting, 403 del bloqueo de IPs, 401 de las
claves de API, 404 del logo) arreglaban **el sitio que escribía la respuesta**. Eso deja fuera
todo lo que no escribimos nosotros, que es la mayoría: **una ruta que no existe**, un método no
permitido, un `Content-Type` que no se acepta. Medido en producción — `/Account/Login`,
`/api/app/no-existe` y cualquier URL inventada respondían `302` a `/Error?httpStatusCode=404`.

- Va registrado **justo después de `UseErrorPage()`**, nunca antes: tiene que correr **por
  dentro** para poner el cuerpo mientras la respuesta vuelve, antes de que `StatusCodePages` la
  mire. Registrado por fuera no haría nada **y no fallaría nada**.
- ⚠️ **Discrimina por `Accept`.** Si el cliente pide `text/html` —una persona con un
  navegador— se deja pasar y la página de error de ABP sigue saliendo, que ahí es lo correcto.
  El JSON es para quien no puede leer una página.
- No pisa a los que ya escribían cuerpo: se abstiene si la respuesta ya trae `ContentType` o
  `ContentLength`.
- El texto es **genérico a propósito** (`"Not found."`). Estas respuestas se dan sin
  autenticar; detallar el motivo es lo que convierte un 404 en un oráculo (§6.98).
- ⚠️ **El detector de escaneo NUNCA estuvo afectado por el 302**, y conviene saberlo por si
  alguien se plantea quitar esto creyendo que arregla aquello:
  `AspNetCoreAuditLogContributor` lee `Response.StatusCode` dentro del ámbito de auditoría
  (`app.UseAuditing()`), que va **por dentro** de `UseErrorPage()`. En `AbpAuditLogs` queda
  grabado el **404**, no el 302, así que el bloqueo automático siempre contó esas peticiones
  como fallos (§6.15). Verificado en `refs/abp`.

### 5.1 Angular: una respuesta que llega y una pantalla que no se entera

⚠️ **TRAMPA** — costó cuatro despliegues encontrarla. Un componente puede recibir la
respuesta HTTP correcta y **no repintarse**: los campos se actualizan en memoria y la
vista se queda congelada en el estado anterior. Pasó en `/telegram`, donde la captura de
red mostraba `200` con los datos buenos mientras la pantalla seguía diciendo
«Consultando…».

Ocurre cuando el callback corre **fuera de la zona de Angular** (la pila de OAuth de ABP
puede provocarlo). El síntoma engaña: parece un fallo de red o de API, y no lo es.

- **Solución**: aplicar los cambios de estado dentro de `NgZone.run(...)` y forzar
  `ChangeDetectorRef.detectChanges()`. Ver el helper `aplicar()` en
  `angular/src/app/telegram/telegram-link.component.ts`.
- **Cómo diagnosticarlo rápido**: hacer que la pantalla muestre su propio estado
  (`ngOnInit` → `pidiendo` → `respuesta recibida`). Si el rastro se queda atrás mientras
  la pestaña Red dice `200`, es esto y no otra cosa. Deducirlo desde el servidor es
  imposible: ahí todo se ve perfecto.
- **Corolario**: para un problema que vive en el navegador, la evidencia tiene que venir
  del navegador. Un HAR descarta red/CORS/caché, pero no distingue «no llegó» de «llegó
  y no se pintó».
- ⚠️ **Volvió a pasar en `/metrics`**, que era la última pantalla sin esta protección (es
  anterior al descubrimiento y se quedó atrás). Estaba **congelada en «Cargando
  métricas…»** con todas sus peticiones devolviendo 200. Se detectó por automatización del
  navegador, no mirándola: nadie la había abierto en una sesión de trabajo.
  - Y hay que envolver **cada callback que cambie algo que se pinte**, no solo el
    principal. Con la serie envuelta, los gráficos aparecían pero las secciones que
    dependen de OTRA llamada (`auditoriaDisponible`, que enciende el registro de peticiones)
    seguían sin salir. El arreglo a medias parece funcionar.


## 5.2 Segundo factor (2FA) por Telegram

ABP open source **detecta** que un usuario necesita segundo factor (`SignInManager` devuelve
`RequiresTwoFactor`) pero **deja el flujo sin implementar a propósito**:
`LoginModel.TwoFactorLoginResultAsync()` lanza `NotImplementedException` con el comentario
*«Override this method to add 2FA for your application»*. Lo de aquí es ese override.

- **Proveedor**: `TelegramTwoFactorTokenProvider` hereda de
  `TotpSecurityStampBasedTokenProvider<IdentityUser>` (el mismo que usan por dentro los
  proveedores de correo y SMS). **No guarda nada**: el código se deriva del sello de
  seguridad y de la hora. Eso evita una tabla de códigos pendientes y que un código quede
  escrito en la base. Efecto secundario deseable: cambiar la contraseña invalida al instante
  los códigos en circulación.
- **Registro**: en `IdentityOptions.Tokens.ProviderMap`, no tocando el `IdentityBuilder`.
  ⚠️ El nombre del proveedor aparece en tres sitios que **tienen** que coincidir (registro,
  generación, validación); si no coinciden **nada falla al compilar**, el código
  simplemente nunca valida. Por eso vive en `TelegramTwoFactorConsts.ProviderName`.
- ⚠️ **Solo se sustituye el MODELO de la página de login, no la vista** — así la pantalla
  sigue siendo la de ABP y se actualiza con el módulo. Se hace con
  `[Dependency(ReplaceServices = true)]` + `[ExposeServices(...)]` (ver
  `AuxiliarContableLoginModel`). La clase base es **`OpenIddictSupportedLoginModel`**, no
  `LoginModel`: heredar de `LoginModel` compila igual pero se pierde el manejo del
  `ReturnUrl` de OpenIddict y el login deja de completar el flujo de autorización.
- ⚠️ **Desvincular Telegram APAGA el segundo factor de forma explícita.** Identity solo lo
  pide si hay algún proveedor válido, y el de Telegram deja de serlo sin chat: si el flag se
  quedara puesto, el usuario leería «activado» mientras entra solo con la contraseña. Es una
  degradación silenciosa, y hay un test que la vigila.
- **No se ofrece «confiar en este dispositivo»** (`rememberClient: false`): esa cookie
  desactiva el segundo factor en ese navegador durante días, justo lo contrario de lo que
  busca quien lo activa.
- La página `Account/TwoFactor` sí es propia (el módulo no trae equivalente). El código es
  TOTP, así que recargar o pulsar «reenviar» manda **el mismo** código en vez de invalidarlo.


## Doble envío del login → error 400 → «producción rota»

Dos toques en «Entrar» mandaban dos POST. El primero iniciaba sesión; el segundo llegaba con el
token antifalsificación emitido **sin sesión** y ABP respondía
`AntiforgeryValidationException: … meant for a different claims-based user` → página de error
400. Desde ahí la persona acababa en la raíz de la API, que era la **portada de la plantilla de
ABP** (con «Iniciar sesión» y la lista pública de aplicaciones de OpenIddict): iniciaba sesión en
la API y no volvía a la consola. En local no se reproduce con un doble clic (el segundo envío
cancela el primero antes de que vuelva la cookie); en producción, con latencia, sí.

- `LoginYaIniciadoMiddleware` (host, tras validar la sesión): un POST al login con sesión ya
  iniciada va a su `ReturnUrl` (solo rutas locales: es un parámetro del cliente).
- `wwwroot/global-scripts.js`: cada formulario de las páginas del servidor se envía una vez.
  ⚠️ Sin deshabilitar el botón: el de «Entrar» lleva `name=Action value=Login`.
- `Pages/Index` (raíz de la API) redirige a `App:AngularUrl`.
- Prueba: `e2e/pruebas/acceso.spec.ts` (la de «reenviar el login» reproduce el 400 exacto:
  formulario cargado sin sesión en una pestaña, sesión iniciada en otra, se envía el viejo).
