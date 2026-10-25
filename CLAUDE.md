# CLAUDE.md — IgasConsole

> Consola de Igas: **ABP Framework (tier open source)** + Angular, .NET 10, SQL explícito
> versionado y **sin Docker** (base de datos y tests con **PostgreSQL** local). Nació como
> copia de `abt-template` (`D:/proyectos/perso/abt-template`), sin multi-tenancy.
> La solución vive en `IgasConsole/`. El código fuente de ABP está clonado en
> `refs/abp` como fuente de verdad para verificar APIs (ver "Regla Cero").

## 1. Qué es el proyecto

Solución ABP en capas (DDD): `Domain(.Shared)` → `Application(.Contracts)` →
`EntityFrameworkCore` (infraestructura) → `HttpApi(.Host)`. **Una sola organización**
(`MultiTenancyConsts.IsEnabled = false`; el módulo de tenants de ABP sigue instalado pero
inactivo), con Identity + OpenIddict + Angular UI. La lectura compleja usa **SQL crudo
versionado**; la escritura y el modelo de dominio usan **EF Core / LINQ**.

### 1.1 Roles y jerarquía — LA REGLA MÁS IMPORTANTE DEL PROYECTO

| Rol | Nombre en BD | Nivel | Quién |
|---|---|---|---|
| Developer | `admin` (el de ABP) | 3 | El desarrollador. Todo, e **invisible** para los demás |
| Owner | `owner` | 2 | El dueño del negocio. Gestiona Administradores |
| Administrador | `administrador` | 1 | Gestiona Usuarios |
| Usuario (y cualquier rol creado a mano) | `usuario` | 0 | Usa los módulos |

- Cada persona **ve, toca y asigna solo lo que está estrictamente por debajo de su nivel**
  (más verse a sí misma). El Developer lo alcanza todo. Lo que no se alcanza responde
  **404, no 403** — un 403 confirmaría que la cuenta existe.
- **El nivel es un DATO del rol** (propiedad extra `Nivel`, enum `NivelDeRol`, declarada en
  `IgasConsoleModuleExtensionConfigurator`: la pantalla de Roles de ABP la enseña sola), no se
  deduce del nombre. `NivelesDeRoles` (caché) da el mapa nombre → nivel; el seed lo pone a los
  cuatro propios. Crear/editar un rol solo con nivel por debajo del propio; los propios no
  cambian de nivel; el de Developer es solo de `admin`. ⚠️ `JerarquiaDeUsuarios.NivelActualAsync`
  (ya no hay versión síncrona).
- La regla vive en `Domain.Shared/Identity/IgasRoles.cs` (lógica pura) y
  `Domain/Identity/JerarquiaDeUsuarios.cs`. La aplican los tres servicios que sustituyen a
  los de ABP en `Application/Identity/`: usuarios, roles y permisos. **Cualquier endpoint
  nuevo que liste o toque usuarios tiene que pasar por la jerarquía**; si no, el Owner
  vería al Developer por ahí.
- Los permisos técnicos (métricas, trabajos, seguridad, auditoría, explorador de BD) son
  solo del Developer: además, casi todos enseñan actividad por usuario, incluida la suya.
- `IgasRolesDataSeedContributor` crea los roles `IsStatic` y les da sus permisos
  (`PermisosInicialesPorRol`) **solo al crearlos**: el migrador corre en cada deploy y
  re-sembrar devolvería permisos que el Owner quitó.
- Los permisos de ABP que no aplican (inquilinos, features y ajustes de SMTP/zona horaria)
  están **apagados** en `IgasConsolePermissionDefinitionProvider.PostDefine`: no salen en
  la pantalla de permisos ni se conceden a nadie. «Configuraciones» queda oculta en el menú
  porque se quedó sin pestañas.
- Tests: `test/*.Integration.Tests/Identity/JerarquiaIntegrationTests.cs`.

### 1.2 Brevo (correo, SMS, WhatsApp)

- Correo: `BrevoEmailSender` sustituye el `IEmailSender` de ABP (en el host), así que todo
  lo de ABP que manda correo sale por Brevo. SMS/WhatsApp: `IMensajeria` (dominio) →
  `BrevoMensajeria` (host). Cliente HTTP: `HttpApi.Host/Notifications/Brevo/BrevoClient.cs`.
- Sin `Brevo:ApiKey` no se envía nada y queda un aviso en el log (dev y tests).
- La API key va en user-secrets (dev) / `/etc/zia-console/env` (prod). Si la cuenta
  tiene «Authorised IPs», Brevo responde 401 a IPs no listadas.
- `IEstadoDelCorreo` dice si hay correo de verdad (API key **y** `Brevo:SenderEmail`). Sin
  eso las invitaciones se rechazan y la pantalla lo avisa. Ojo: las implementaciones
  llevan `[ExposeServices]` explícito — el nombre no sigue la convención de ABP.
- **Plantillas de correo** (Scriban, `Domain.Shared/Emails/*.tpl`, registradas en
  `Domain/Emails/IgasEmailTemplates.cs`): el layout **sustituye** al de ABP
  (`StandardEmailTemplates.Layout`), así que cualquier correo de ABP sale con la marca. El
  layout no recibe el modelo, solo el contexto global: renderiza con `PlantillasDeCorreo`
  (host), que le pasa logo y URL. HTML de correo = tablas y estilos en línea; lo que
  escriba un usuario va con `| html.escape`. Test: `PlantillasDeCorreoTests`.

### 1.2.1 Invitaciones (la forma normal de dar de alta a alguien)

- `/invitaciones` (permiso `AbpIdentity.Users.Create`): correo + rol. Solo roles por
  debajo del propio, nunca el Developer. `/registro#t=TOKEN` es público: elige usuario y
  contraseña, y la cuenta nace con el correo **confirmado** (el enlace llegó a ese buzón).
- El token **nunca** se guarda en claro ni va en los argumentos del job: lo genera
  `EnviarInvitacionJob` al enviar y solo queda su SHA-256. Va en el fragmento (`#`) para
  no acabar en logs. Reenviar rota el enlace. El job es transaccional: si Brevo falla, no
  queda un enlace «enviado» que nunca salió.
- `/api/app/registro/*` está en las rutas con límite de credenciales.
- Tests: `test/*.Integration.Tests/Invitaciones/`.
- El manual por rol está en `angular/src/app/ayuda/manual.ts` (pantalla `/ayuda`). Si
  cambia una pantalla o un permiso inicial, actualízalo en el mismo commit.
- Idioma: solo español (`IgasConsoleDomainSharedModule` + `DefaultLanguage = "es"`).

### 1.2.2 Seguridad de la cuenta (correo, teléfono, dos pasos)

- `/seguridad` (cada uno, la suya): verificar correo y teléfono con código y activar la
  verificación en dos pasos. `SeguridadDeCuentaAppService`; un aviso arriba
  (`AvisoSeguridadComponent`) lo pide a quien no tenga correo verificado o teléfono.
- Códigos = proveedores «Email»/«Phone» **de ABP** (`AbpTwoFactorTokenProvider`, no los
  TOTP de ASP.NET): de un solo uso, cifrados en AbpUserTokens, pedir uno anula el anterior.
  Vigencia 5 min (se fija en el host). `LimitadorDeCodigos` pone espera entre envíos,
  techo por hora y bloqueo tras 5 fallos (un SMS cuesta dinero).
- Envío: `IEnviadorDeCodigos` → `EnviadorDeCodigosBrevo` (plantilla `Emails/Codigo.tpl`;
  SMS sin acentos — uno solo pasa el mensaje a UCS-2 y se cobra doble).
- **SMS apagado** (`Brevo__SmsHabilitado`, plan gratis sin créditos): el teléfono se guarda
  sin verificar y el segundo factor va por correo/Telegram.
- Login con dos pasos: `Pages/Account/TwoFactor` (correo, SMS o Telegram, a elegir). Los
  avisos van en la vista: los `Alerts` de ABP no se pintan en el layout de cuenta.
- Alta **sin correo**: se guarda `usuario@sin-correo.invalid` (RFC 2606; el emisor de correo
  lo descarta). ⚠️ No usar `ShouldChangePasswordOnNextLogin`: en ABP open source no hay
  pantalla para cambiarla y deja a la persona fuera («no se permite el acceso»).
- **Códigos de recuperación** (10, de un solo uso, los de ASP.NET Identity en AbpUserTokens):
  se generan al activar los dos pasos y se enseñan UNA vez; `TwoFactor` los acepta con
  «usa un código de recuperación» (esa vista no manda ningún código al abrirse).
- **Rescate por un administrador**: `SeguridadDeUsuariosAppService.QuitarSegundoFactorAsync`
  (permiso Users.Update + jerarquía; uno mismo no, 404). Queda en el registro de seguridad.
- Tests: `test/*.Integration.Tests/Seguridad/`. El módulo de tests registra a mano los
  proveedores de ABP (no carga el módulo web de Identity).

### 1.2.3 Límites de peticiones (lo que ya rompió)

- Login y rutas que prueban credenciales o mandan códigos: 10/min por IP (partición `auth:`).
- `/connect/token`: partición propia, `TokenPermitLimit` = 120/min. ⚠️ El Angular pide token
  en CADA carga completa de página; con los 10/min del login, recargar unas veces (o una
  oficina detrás de una IP) daba 429 y mandaba al login.
- De `/api/app/seguridad-de-cuenta` solo van en el límite estricto las rutas que envían o
  prueban un código: el estado lo consulta el aviso en cada carga de la app.

### 1.2.4 Pruebas de pantalla (`IgasConsole/e2e/`, Playwright)

Desbordes y pestañas escondidas a **390 / 768 / 1024 / 1280 px** (teléfono, tablet vertical,
tablet horizontal, escritorio), contraste en modo oscuro, fundido del tema, Usuarios,
Seguridad, Ayuda y registro inválido, contra la app LOCAL arrancada (ver `e2e/README.md`).

- **Toda pantalla nueva** entra en `RUTAS` (`e2e/pruebas/ayudas.ts`) y se revisa además a ojo
  (capturas) en teléfono y tablet, en claro y oscuro, antes de darla por terminada.
- Menú desplegable hasta **900 px** (el corte está en el `.ts` y el `.scss` de `IgasLayout`):
  con 760 una tablet vertical se quedaba con el menú fijo comiéndose un tercio.
- `.ig-pestanas` en estrecho pasan a otra fila; nunca deslizar con la barra oculta (esconde
  pestañas sin pista). La prueba de desbordes lo vigila en todas las rutas.
- Tablas: `tabla-apilable` apila en teléfono; si tiene muchas columnas (litros, historial),
  `tabla-apilable-tablet` apila hasta 1200 px. Nada debe deslizar dentro de su caja en tablet.
- ⚠️ `--ig-borde` es un borde COMPLETO (`1px solid …`): `border-bottom: var(--ig-borde)`, nunca
  `1px solid var(--ig-borde)` ni como fondo — el navegador descarta la regla en silencio y el
  divisor no se pinta. Lo vigila `src/styles/tokens.spec.ts`. Van en serie
con UNA sesión: el refresh token rota y dos navegadores con la misma sesión la revocan.
⚠️ En estilos de componente no uses `:root[...] .algo` para el tema: la encapsulación de
Angular también marca `:root` y la regla no casa nunca. Usa `:host-context(html[data-tema='oscuro'])`.

### 1.2.5 Auditor de controles volumétricos (el módulo de negocio)

`/volumetricos`: se sube el JSON **mensual** (v4.2, expendio) que la estación manda al SAT y
se audita al momento; el PDF (resumen + uno por producto) es lo que se manda al cliente.

- **Motor** (`Domain/Volumetricos/MotorDeAuditoria.cs`, lógica pura con `decimal`): produce un
  `ResultadoAuditoria` (`Domain.Shared`) que se **guarda tal cual** en la entidad
  (`ResultadoJson`). Pantalla y PDF solo lo pintan: nunca recalculan, así que no pueden
  contradecirse, y una auditoría vieja conserva los umbrales y la `VersionMotor` con que se
  hizo. Si cambian las reglas, sube `MotorDeAuditoria.Version`.
- Reglas que no se ven leyendo: `NoCalculable` / `SinMovimientos` **nunca** cuentan como
  conciliado; no se mezclan productos ni unidades; un CFDI citado en el JSON NO es un CFDI
  validado; la bitácora es de la estación y no se atribuye a un producto. Cada cifra lleva la
  ruta del dato en el JSON.
- Umbrales (0,5 % fiscal / 1,5 % penal, los pidió el cliente) en ajustes globales con su
  **fuente**, que sale en cada PDF. Permisos: `IgasConsole.Volumetricos` (ver),
  `.Subir`, `.VerTodas`, `.Eliminar`, `.Umbrales`. Quién ve qué: ver 1.2.6.
- **.zip / .rar** con varios JSON (`PaqueteDeReportes`, Domain, con **SharpCompress** —MIT—
  porque .NET no abre RAR): cada JSON es una auditoría con su propio original; uno dañado se
  rechaza con su motivo sin tumbar el resto (`SubidaVolumetricaDto`). Se detecta por la firma,
  no por la extensión. ⚠️ Topes contra «bombas zip» aplicados MIENTRAS se lee: entradas,
  bytes por JSON y total descomprimido (`VolumetricosConsts`). Un nivel de zip dentro de zip.
  El .rar de los tests es sintético y está en el repo (`Volumetricos/Datos/`): crear RAR
  requiere WinRAR.
- **Litros reportados al SAT** (`CalculadoraDeLitros`, `GetResumenDeLitrosAsync`, PDF
  `GeneradorPdfLitros`): vendidos, facturados, comprados, inventario y merma por producto,
  desde el resultado guardado. ⚠️ El mensual NO trae inventario inicial: sale del cierre del
  mismo producto en la auditoría del mes anterior de la misma estación (mismo permiso). Sin
  ella la merma queda vacía con su explicación; nunca se inventa. La ven equipo y cliente.
- **Día por día** (`DesglosarPorDia` en el motor → `ProductoAuditado.Dias` y `BitacoraPorDia`;
  `DesgloseDiario` → `GetDesgloseDiarioAsync`, PDF `GeneradorPdfDiario`, pantalla `diario.component`):
  lo pidió un cliente. El mensual no trae mediciones diarias, pero cada CFDI trae su
  `FechaYHoraTransaccion` (en los reales la suma por día da exacto el total declarado). ⚠️ Son
  litros DOCUMENTADOS; el inventario de cada día se cuenta HACIA ATRÁS desde el cierre del mes.
  `VOL-DIA-001` (media): el mes cuadra pero algún día el tanque queda negativo; `VOL-DIA-002`
  (informativo): 3+ días seguidos sin ventas de un producto mientras otros venden. Lo ven equipo
  y cliente. Las auditorías de motor anterior a 2026.10.1 no lo guardaron: se calcula al pedirlo del
  JSON ORIGINAL con sus mismos umbrales (`ResultadoConDiasAsync`), sin reescribir lo guardado.
- El original va a BLOB (`volumetricos-originales`). `LectorJsonEstricto` rechaza JSON mal
  formado con línea/posición y claves duplicadas. Tope 30 MB en la app; nginx sube el suyo
  **solo** en `/api/app/auditoria-volumetrica/subir` (`deploy/nginx.conf`).
- PDF con **QuestPDF**, licencia Community (gratis por debajo de 1 M USD/año de ingresos; si
  el negocio lo supera hay que comprarla). `GeneradorPdfVolumetrico` en Application.
- ⚠️ Los JSON de clientes (RFC, nombres) **no entran al repo**: los tests usan `JsonDePrueba`
  (Domain.Tests) y `reporteVolumetricoSintetico` (e2e). Guías del SAT en `refs/` (ignorado).
- Tests: `test/*.Domain.Tests/Volumetricos/`, `test/*.Integration.Tests/Volumetricos/`, e2e.

### 1.2.6 Organizaciones — QUIÉN VE QUÉ auditorías

`/organizaciones` (permiso `IgasConsole.Organizaciones`): un cliente = una organización con
**estaciones** (por número de permiso del JSON, normalizado con
`OrganizacionConsts.NormalizarPermiso`) y **miembros** (usuarios).

- **Cada archivo guarda su organización** (`AuditoriaVolumetrica.OrganizacionId`), elegida al
  subir (`SubirAsync(archivo, organizacionId, prueba)`); sin elegir, la que tenga dada de alta
  la estación (si está en una sola). Null = **prueba**: solo la ve quien la subió. ⚠️ Ya NO se
  deduce del permiso en cada consulta: añadir/quitar una estación no mueve el historial; para
  eso está `CambiarOrganizacionAsync` (equipo; pasar a prueba solo quien lo subió).
- Quién ve qué (`ConsultaVisibleAsync` en `AuditoriaVolumetricaAppService`, con una `Mirada`
  por petición): el **equipo** (Owner y Administrador = `VerTodas`) ve todo lo que tiene
  organización más SUS pruebas (no las de otros del equipo); un **cliente**, lo de sus
  organizaciones, lo haya subido quien sea; el **Developer**, todo. Un cliente sube (permiso
  `Subir`, v4) solo a sus organizaciones y nunca pruebas.
- Lo del **Developer** no existe para nadie más (`DevelopersOcultosAsync`) ni sale en «quién
  puede verlo» (`DevelopersAsync`). Para listar PERSONAS (miembros, candidatos) sigue
  `UsuariosFueraDeAlcanceAsync` y añadir/quitar exige `ExigirGestionableAsync` (404).
- Historial (`GetListAsync` → `HistorialVolumetricoDto`, con conteos por resultado;
  `GetNavegacionAsync` → organizaciones con problemas primero según el reporte MÁS RECIENTE de
  cada estación; `GetPersonasAsync`). La pantalla (`historial.component`) guarda los filtros en
  la URL. El reporte es su propia página, `/volumetricos/reporte/:id` (ruta HIJA: el auditor
  sigue vivo y oculto debajo, así el historial conserva lo cargado al volver); no está en el
  menú, se llega desde el historial, al subir o desde Actividad. Anterior/siguiente van en el
  estado de la navegación (`EstadoDelReporte`).
  ⚠️ Un `Guid? xxxId` suelto en un app service de ABP va a la RUTA (`/personas/{organizacionId}`)
  y choca con `{id}`: se pasa en un DTO de entrada.
- **Un solo vocabulario** para el resultado: «Con errores / Con observaciones / Sin problemas»
  (`ResultadoEnPalabras` en Domain.Shared; `palabras.ts` en el front). Historial, reporte,
  vista del cliente y PDF lo sacan de ahí; el `EstadoTexto` que guardó el motor ya no se enseña.
- **Repetidos**: el mismo contenido (SHA-256) auditado con el mismo motor y umbrales no se
  vuelve a auditar (`SubidaVolumetricaDto.Repetidos`); con umbrales o motor nuevos, sí.
- **Borrar se deshace** (`RestaurarAsync`: quien lo borró, `MinutosParaDeshacer`). Por eso
  `DeleteAsync` ya no borra el original: lo purga `OriginalesBorradosPurgeWorker` (host) pasados
  `DiasParaPurgarOriginalesBorrados`.
- Cuando un **cliente** sube, se deja un aviso en la campana para `VerTodas` (una fila por
  subida, no por archivo).
- ⚠️ Las hojas del historial son `<dialog>` modales (capa superior): los avisos y
  confirmaciones de ABP quedan DEBAJO. Cerrar la hoja antes de confirmar; errores, en la hoja.
- A un cliente no se le dice quién del equipo subió cada archivo (`SubidoPor` vacío y
  `SubidoPorEquipo`; filtra por sus compañeros o por «Equipo ZIA», nunca por un id del equipo).
- **Vista del cliente**: `RedactorParaCliente` (Application) cuenta el resultado guardado en
  lenguaje llano (`ReporteClienteDto`: sin rutas, fórmulas, códigos ni bitácora técnica; solo
  hallazgos altos y medios). ⚠️ Es lo ÚNICO que el servidor le manda a un cliente:
  `Resultado` (lo técnico) va null si no tiene VerTodas, y el PDF técnico responde 403. Su PDF
  es `GetPdfClienteAsync` (`GeneradorPdfCliente`). El equipo recibe ambos y la pantalla deja
  alternar «Vista técnica / Vista del cliente».
- **Alta dentro de una organización**: un cliente (nivel Usuario) no se crea sin ella
  (`MembresiasDeOrganizacion`). A mano, llega en `ExtraProperties["OrganizacionId"]` del DTO
  de ABP y lo aplica `IgasIdentityUserAppService` (también al editar un cliente sin
  ninguna); por invitación, va en la `Invitacion` y se aplica al aceptar. ⚠️ Se comprueba
  ANTES de crear: el `CreateAsync` de ABP guarda por dentro y fuera de un POST
  transaccional la cuenta quedaba hecha aunque luego se rechazara.
- **Papel dentro de la organización** (en la membresía, no un rol global, como GitHub/Clerk):
  `RolEnOrganizacion` Administrador / Miembro / Lector; qué permite cada uno está SOLO en
  `PermisosEnOrganizacion` (tabla de casos en `PermisosEnOrganizacionTests`). Lector no sube.
  El Administrador gestiona a SU gente en `/mi-organizacion` (`MiOrganizacionAppService`):
  invitar (al rol de cliente y a su organización), cambiar papel, quitar; nunca ve ni toca al
  equipo, otras organizaciones ni roles/permisos globales (404). No se puede quedar sin el
  último administrador. La invitación guarda el papel (`Invitacion.RolEnOrganizacion`).
- **Autorizador** (`Application/Autorizacion`): el ÚNICO punto que decide quién ve qué en el
  auditor y en las organizaciones (`QuienMira`: equipo, Developer y membresías con papel). Se
  calcula en cada llamada: ⚠️ no guardarlo en la instancia (un servicio que vive más que una
  petición vería membresías viejas; pasó en los tests). Si algún día hace falta un motor
  externo (OpenFGA), el cambio queda ahí dentro.
- ⚠️ Estaciones y miembros son hijas del agregado con **clave compuesta**: guardar con
  `CurrentUnitOfWork.SaveChangesAsync()`, NO `UpdateAsync` — `Update` las marca como
  modificadas y las nuevas nunca se insertan.
- ⚠️ En los tests la autorización es «todo permitido»: `AlcanceSegunRol` (módulo de tests)
  sustituye a `AlcanceDeAuditorias` para que el rol `usuario` haga de cliente.
- **Validación por permiso CRE** (octubre 2026; `ResolverAsync` en `AuditoriaVolumetricaAppService`):
  un **cliente** (sin `Volumetricos.SubirCualquierEstacion`) solo sube JSON cuyo `NumPermiso` sea de
  una estación de una organización donde puede subir, y su `RfcContribuyente` = `OrganizacionEstacion.Rfc`
  si está. Se valida CADA JSON (también dentro de .zip/.rar), aunque mande `organizacionId`, y ANTES de
  guardar el blob; el rechazo queda en `AppEventosVolumetricos` (`BitacoraVolumetrica`, en su propia
  unidad de trabajo para que sobreviva a la excepción) y sale en Actividad. Organización **sin
  estaciones**: el primer JSON la propone (`OrganizacionEstacion.PendienteDeConfirmar`) y sus archivos
  quedan **retenidos** (`AuditoriaVolumetrica.PendienteDeConfirmar`: solo los ven el equipo y quien los
  subió) hasta `ConfirmarEstacionAsync` / `RechazarEstacionAsync` (rechazar borra los retenidos). El
  **equipo** (`SubirCualquierEstacion`, v12) sube en bloque: cada JSON a la organización de su permiso,
  lo que no casa queda sin asignar (`SubidaVolumetricaDto.Resultados`); con organización fijada y permiso
  que no casa, `RequiereConfirmacion` hasta reenviar con `confirmarDiscrepancias`.
  ⚠️ `NumPermiso` es **único** entre organizaciones (índice único; `UnicidadDeEstaciones` lo dice en
  palabras). Por eso borrar una organización (lógico) le quita sus estaciones. Tests:
  `PermisoCreIntegrationTests`, `EstacionesConPermisoCreTests`.
- Permisos nuevos a roles que ya existen: `PermisosNuevosPorVersion` (se aplica una vez por
  versión, apuntada en el ajuste `IgasConsole.Permisos.VersionSembrada`).
- Tests: `test/*.Integration.Tests/Organizaciones/`.

### 1.2.7 Actividad — quién hizo qué

`/actividad` (permiso `IgasConsole.Actividad`, Owner y Administrador): `ActividadAppService`
cuenta en frases lo que ya se registra, por día y con filtros (persona, organización, tipo).

- Fuentes: `AbpAuditLogActions` (qué reporte abrió/descargó, a quién invitó, cambios en
  organizaciones y usuarios; lee los PARÁMETROS de cada llamada), `AbpSecurityLogs`
  (entradas, contraseñas, dos pasos), y las tablas propias (reportes subidos/borrados,
  invitaciones aceptadas). Lectura en `IRegistroDeActividad` (EFCore).
- ⚠️ El registro de llamadas se purga a los 30 días (`MonitoringConsts.RetentionDays`); lo de
  tablas propias no. Nombra lo BORRADO (filtro de soft delete apagado al buscar nombres).
- Jerarquía por ACTOR: solo sale lo que hicieron personas que uno alcanza (y él mismo). El
  servicio lleva `[DisableAuditing]`: mirar la actividad no es actividad.
- ⚠️ Como la actividad lee los parámetros, **cualquier DTO con un secreto lleva
  `[DisableAuditing]`** en esa propiedad: sin eso ABP lo guardaba en claro (pasó con la
  contraseña del registro por invitación). `SecretosFueraDelRegistroTests` lo vigila.
- Tests: `test/*.Integration.Tests/Actividad/` (siembran con `IAuditingStore` e `ISecurityLogStore`).

### 1.2.8 Estaciones — el software para gasolineras

`/estaciones/*` (menú «Estaciones»): el producto que se va a construir feature a feature
(`docs/producto/inventario-de-features.md`). Hoy las **organizaciones y estaciones son reales**
(`EstacionesAppService.GetContextoAsync`: el equipo con `Estaciones.TodasLasOrganizaciones` recibe
todas; los demás, las suyas vía `Autorizador`) y **la operación es real** salvo en la organización de
demostración, donde es de ejemplo (`estaciones/datos.ts`, generada con semilla y movida cada segundo en
el navegador; ver 1.2.9).

- Un permiso por pantalla (`IgasConsolePermissions.Estaciones.*`, a Owner y Administrador en la v6;
  `Configuracion` y `Nodo` en la v7, `Comunicaciones` en la v8); `MapaDeFeatures`, `Nodo` y
  `Comunicaciones` son del equipo. Comunicaciones (G5–G7) mira toda la red, no una estación: sus
  datos de ejemplo están en `estaciones/telecom.ts`. Un cliente aún no
  tiene ninguno.
- El inventario (`datos.ts` → `FEATURES`) dice de cada feature si **ya existe** en la consola
  (`existe`: su ruta real), si se ve en el prototipo (`pantalla`) o si no tiene pantalla aún. Una
  feature nueva va en el doc del inventario Y ahí, en el mismo commit.
- Armazón `EstacionesComponent` (sin encapsulación, clases `es-*`): barra pegada arriba con la
  organización (‹ › y lista, «Todas» para el equipo) y los botones de estación. **No hay
  interruptor de vista: lo deciden los permisos** (`TodasLasOrganizaciones` → todas y «Todas»;
  `MapaDeFeatures` → etiquetas A2, B1…).
- ⚠️ `.ig-main` usa `overflow: clip`, no `hidden`: `hidden` lo vuelve contenedor de desplazamiento
  y ningún `position: sticky` de dentro se pega (pasaba con esta barra y la del historial).
- Un elemento claro a propósito (el ticket impreso) lleva `data-claro-a-proposito` para la prueba
  de modo oscuro.
- Cada pantalla imita el objeto real: caras de dispensario (`.es-display`), tanque horizontal visto
  de frente (`TanqueVistaComponent`: dibuja la ALTURA, no el % de volumen — `alturaEnCilindro`),
  poste de precios, balance como ecuación con `ToleranciaComponent`, curva del día
  (`CurvaComponent`). Piezas compartidas en `estaciones/comun.ts` + hoja del armazón; lo propio de
  cada pantalla, en su `.scss`. Tokens de aparato (mismos en los dos temas): `--ig-pantalla`,
  `--ig-digito`, `--ig-agua`; el texto sobre el color lleno de un producto, `--es-sobre-producto`.

### 1.2.9 Nodos de estación — el control volumétrico con datos reales

Tres programas y una librería: `IgasConsole.Nodo.Contratos` (sin ABP: formatos que viajan, corrección
a 15 °C —tabla 54B—, huella de la bitácora, firma ECDSA de paquetes, cliente HTTP), `IgasConsole.Nodo`
(el servicio de la estación: SQLite, drivers, cola, pantalla local en el puerto 8686) e
`IgasConsole.Nodo.Guardian` (el único servicio del sistema operativo: activa la licencia, baja, arranca,
vigila y actualiza el nodo). Linux y Windows con el mismo código; tests en `test/IgasConsole.Nodo.Tests`
(sin PostgreSQL) y `test/*.Integration.Tests/Estaciones/NodosIntegrationTests.cs`.

- **Organización de demostración** (`Organizacion.EsDemostracion`): la ÚNICA con operación simulada en
  el navegador. La crea el migrador una vez («Demostración ZIA», sin miembros: solo la ve el equipo)
  y no admite licencias. Las demás enseñan lo que suben sus nodos (`OperacionDeEstaciones`), y sin
  nodo salen vacías. ⚠️ Nunca inventar un dato en las pantallas reales: tanque sin lectura = «sin
  lectura», nodo caído = bombas «sin comunicación», balance sin lecturas = no calculable.
- **Licencia → nodo**: la clave (`IGAS-XXXXX-…`) solo se guarda hasheada, sirve una vez y caduca a
  los 30 días. Al activarse nace un `NodoDeEstacion` con su propia clave (hash; `Authorization: Nodo
  {id}.{clave}`). Reactivar con otra clave revoca el nodo anterior. ⚠️ La clave de licencia se
  normaliza (mayúsculas, sin guiones) y la del nodo NO: `GestorDeNodos.Hash` vs `HashExacto`.
- **API de nodos** (`/api/nodo/*`, `NodoController` en el host, regla en `PuertaDeNodos`): anónima y
  fuera del registro de peticiones. ⚠️ Sin credencial contesta 401 a mano; la excepción de
  autorización de ABP redirigiría al login (302) y el nodo recibiría HTML.
- **Ingesta idempotente**: cada registro trae el consecutivo de la cola del nodo y se descarta lo
  que ya se tiene (`NodoDeEstacion.UltimoConsecutivo`, índice único `(NodoId, Consecutivo)`). La nube
  re-verifica que cada registro de bitácora enlace con el anterior (`BitacoraRotaEn`).
- **Balance a 15 °C**: stock (`Volumen15` del nodo o de la consola) y ventas (`Litros15`: el nodo
  corrige cada venta con la temperatura de su tanque). ⚠️ Mezclar litros naturales con stock a 15 °C
  daba ~0.8 % de diferencia fantasma. Las consolas en unidades de EE. UU. compensan a 60 °F, no a
  15 °C: ahí se recalcula con la densidad del producto.
- **Actualizaciones**: `deploy/nodo/empaquetar.sh` (publica y firma; la llave privada vive en
  `~/.igas/firma` y NUNCA entra al repo: los nodos instalados solo aceptan su pareja pública) y
  `publicar.sh` (a `Nodos__CarpetaDePublicaciones`). Publicar no actualiza: se elige «versión
  estable» en `/nodos` (permiso `Nodos.Versiones`, solo Developer). El guardián revisa cada 15 min,
  espera a que no haya venta en curso y vuelve a la anterior si la nueva no arranca. **Actualizar ya / programar** (`NodoDeEstacion.ActualizarEn`, botón en `/nodos` y en la pantalla local):
  hasta esa hora la nube le ofrece la versión que ya tiene; al llegar, el latido trae `ActualizarYa` y el
  nodo (`Nucleo/Reinicio.cs`) termina al guardián con la base ya cerrada: systemd/Windows levantan el
  servicio y el guardián revisa al arrancar. Así sirve con guardianes viejos (el guardián NO se actualiza
  solo; cambiarlo exige reinstalar). Cada aviso se atiende una vez (`actualizacion-atendida.txt`).
- **Instalación**: `/instalar#l=CLAVE` (pública) arma el comando de Linux (`instalar.sh`) o el
  `Instalar-ZIA.cmd` de Windows (que corre `instalar.ps1`); los dos guiones se sirven desde la API
  con su dirección ya puesta (`__NUBE__`). Se pueden repetir sin gastar otra licencia.
- **Pantalla local** (`http://equipo:8686`, `Local/web/`): la estación con el MISMO aspecto que la consola
  (Resumen, Pista, Tanques, Recepciones, Ventas, Control volumétrico, Nodo), sin internet. Usa las hojas
  de la consola compiladas por `angular/scripts/estilos-del-nodo.mjs` (salida en el repo, `Local/web/assets/`)
  y copia el marcado de `estaciones/pantallas/*.html` en `app.js`. ⚠️ Si cambias el design system, el
  armazón o una pantalla de Estaciones, corre el script y ajusta `app.js` en el mismo commit.
- **Respaldo (A12)**: el nodo (`Nucleo/Respaldos.cs`) hace cada día `VACUUM INTO` + gzip en
  `datos/respaldos` (14) y lo sube a `/api/nodo/respaldo` (BLOB `respaldos-nodos`, 3 por estación;
  nginx admite 64 MB solo en esa ruta). Un equipo SIN `nodo.db` y con identidad baja el último de su
  estación antes de abrir la base (`RespaldosDeNodos.UltimoAsync`: marca subido lo que la nube ya tiene
  y continúa la cadena de la bitácora; mismo nodo → sube `sqlite_sequence` por encima de lo que ya hay).
- **Avisos**: `VigilanciaDeNodos` (dominio, se prueba sola) decide; `VigilanteDeNodosWorker` (cada min)
  manda a la campana/Telegram (`IAvisador`, permiso Nodos) y por correo a los ADMINISTRADORES de la
  organización (`EnviarAvisoDeEstacionJob`). Cada cosa una vez, con su marca en la fila (`AvisoDe*En`).
- **NIP del supervisor**: `ConfiguracionDeEstacion.NipSupervisor` (PBKDF2, nunca en claro) se fija con
  `PonerNipAsync`; la pantalla de la consola NUNCA lo recibe (`Mapear` lo borra y `GuardarInstalacion` lo
  conserva). El nodo lo pide en `/api/local/actualizar` y bloquea 5 min tras 5 fallos (`IntentosDeNip`).
- Drivers: consola de sondas Veeder-Root (`ProtocoloTls`, escrito contra el manual 576013-635; probar
  con `igas-nodo simular-consola`), entrada HTTP genérica (`Local/Receptor.cs`) y simulador. Falta el
  del controlador de pista (DOMS/IFSF) y generar los JSON del SAT (A10).

### 1.2.10 Escritorios remotos — MeshCentral

`/escritorios` (solo el equipo: permiso `IgasConsole.Escritorios*` **y** nivel Administrador o más,
`AlcanceDeEscritorios`). MeshCentral (`mesh.ziacentro.mx`) es la fuente de verdad de equipos y
grupos: no se guardan aquí; la base solo tiene la auditoría del módulo (`AppEventosDeEscritorio`).
Detalle, variables (`Escritorios__MeshCentral__*`) y protocolo en `docs/escritorios-remotos.md`.

- `ClienteMeshCentral` habla con `/control.ashx`. ⚠️ `meshes`/`lastconnects` no devuelven
  `responseid`. Errores sin URL: con la clave de tokens, la URL lleva el token.
- «Conectar» = token `?login=` de un solo uso y 2 min (`TokenDeMeshCentral`, el `encodeCookie` de
  MeshCentral). La clave de tokens (`LoginTokenKey`) es un secreto que entra como cualquier usuario.
- «Conectar» abre el **visor** (`/escritorios/ver/:id`, `VisorComponent`): MeshCentral en un iframe
  con `&zia=visor&hide=63`, que habla con la consola por postMessage (`deploy/meshcentral/custom.js`).
  ⚠️ Requiere `allowedFramingOrigins` en la config de MeshCentral. Si cambias el dominio de la
  consola, cambia también `ORIGENES_DE_LA_CONSOLA` en custom.js.
- Borrar un grupo solo vacío: MeshCentral borra con él sus equipos.
- **Código ZIA** (`CodigoZia`): 9 cifras derivadas del id del equipo, sin guardar; ⚠️ no cambiar la
  fórmula (los clientes lo tendrán apuntado). «Conectar por código» y los grupos con «Pedir permiso
  al cliente» (bits `consent` del grupo) piden el «Permitir»; enviar archivos va por un túnel de
  archivos propio de custom.js. Detalle en `docs/escritorios-remotos.md`.
- **Página de soporte** (`ziacentro.mx/soporte`, repo Roque98/ZIA): inventa el código, lo enseña y lo
  mete en el instalador (`&tag=`, queda en `node.agent.tag`; ese manda sobre el derivado). Descarga por
  `SoporteController` (`/api/soporte/*`, anónimo), que redirige a MeshCentral con el grupo de
  `Escritorios__Soporte__*`.
- Tests: `MeshCentralFalso` (integración) y `ClienteMeshCentralTests` (WebSocket falso).

### 1.2.11 App móvil (Roque98/ZIA-App)

Expo / React Native, refleja la consola (sin Estaciones ni nodos). Entra con su propio cliente
OpenIddict `IgasConsole_Mobile` (`OpenIddictDataSeedContributor`): nativo, público, redirect
`zia://auth`, `authorization_code` + `refresh_token` y **PKCE obligatorio** (un esquema propio lo
puede registrar otra app). ⚠️ El `UpdateAsync` de ABP reescribe las «requirements» en cada siembra:
`RequirePkceAsync` las vuelve a poner. Test: `ClienteAppMovilIntegrationTests`. La app usa los
mismos endpoints que el Angular: si cambia un DTO o un permiso, revisa también la app.

### 1.3 Design system (el aspecto de TODO el front)

El prototipo `docs/referencia-ui/panel-administracion.html` es la referencia visual.

**La marca es ZIA** (ZIA Integración, marca propia; iGAS era de otra persona y ya no sale en
nada visible). Isotipo y logotipo («ZÍA», dorado) salen de los SVG oficiales del repo de ZIA,
copiados en `docs/marca/`, con `docs/marca/rasterizar.cjs` (PNG para Angular, host, nodo e
instalador, y el `zia.ico`). Acento: verde azulado `#1a7f7f` (el `#1e8a8a` de su sitio, apenas
más oscuro para que el texto BLANCO encima pase AA) con texto blanco; el dorado `#A69140` solo
para la palabra de la marca en la cabecera oscura de los PDF.

- ⚠️ Conservan «iGAS»/«IGAS» A PROPÓSITO (cambiarlos rompe lo ya instalado): el prefijo de
  licencia `IGAS-` (`NodoConsts.PrefijoDeLicencia`), las cabeceras `X-Igas-*`, las carpetas
  `iGAS\Nodo` / `igas-nodo` y el servicio `igas-nodo`, la regla de firewall «Nodo iGAS (pantalla
  local)», el `AssemblyName` `Instalar-iGAS` (lo copia `empaquetar.sh`) y los nombres internos
  `IgasConsole.*`. Cada uno lleva su nota en el código.
- Lo que vive en la BASE se migra al sembrar: la organización «Demostración iGAS» se renombra
  (`OrganizacionDeDemostracionDataSeedContributor`) y la fuente de los umbrales guardada tal cual
  pasa a la de ZIA (`CambioDeMarcaDataSeedContributor`). Un acento o nombre guardado a mano en
  «Marca» no se toca: lo cambia el Owner.

- Tokens, tipografía (Sora, servida local) y componentes: `angular/src/styles/igas/`.
  **Ningún componente escribe colores, radios ni sombras a mano**: usa `var(--ig-*)`. Si
  falta un token, se añade en `_tokens.scss` con su motivo.
- Piezas propias con prefijo `.ig-` (`ig-btn ig-btn-principal`, `ig-panel`, `ig-pestanas`,
  `ig-tabla`, `ig-etiqueta`, `ig-alerta`, `ig-vacio`, `ig-pasos`…). Catálogo vivo:
  `/design-system` (solo desarrollo).
- `_puente-abp.scss` hace que las clases de Bootstrap y los componentes de ABP (usuarios,
  roles, modales, avisos, confirmaciones) se vean igual. LeptonX inyecta sus hojas DESPUÉS de
  la nuestra: por eso el puente va bajo `html` / `html:root`.
  - ⚠️ Modo oscuro: `bootstrap-dim.css` y `ng-bundle.css` (LeptonX) fijan fondos a `#fff`
    en variables (`--bs-modal-bg`…), usan selectores con una clase más o `!important`. Si
    algo de ABP sale blanco/rosa/morado en oscuro, mira qué regla gana (CDP
    `CSS.getMatchedStylesForNode`) y súbele el peso ahí, no a ciegas.
- El layout (menú lateral, barra superior) es propio: `app/layout/`, registrado como el
  layout «application» de ABP. Los iconos del menú son Bootstrap Icons de trazo (`bi bi-*`);
  comprobar que existen en `node_modules/bootstrap-icons` (uno inexistente no da error).
- Las páginas del servidor (login, recuperar contraseña) no comparten hoja con el SPA: su
  aspecto está en `HttpApi.Host/wwwroot/global-styles.css`, con los mismos valores de token.
- Accesibilidad: `--ig-gris` no es para texto (3,3:1); el texto de apoyo es
  `--ig-texto-secundario`. El acento nunca como color de texto sobre blanco.

## 2. Comandos

Todos se ejecutan desde `IgasConsole/` (la carpeta de la solución):

```bash
# Build (con TreatWarningsAsErrors — un warning rompe el build)
dotnet build IgasConsole.slnx -c Debug

# Tests
dotnet test IgasConsole.slnx                                             # toda la suite
dotnet test test/IgasConsole.Architecture.Tests/*.csproj                 # arquitectura
dotnet test test/IgasConsole.Integration.Tests/*.csproj                  # integración (PostgreSQL + Respawn)
dotnet test test/IgasConsole.EntityFrameworkCore.Tests/*.csproj          # unit/EF (SQLite en memoria)

# Migraciones (desde src/IgasConsole.EntityFrameworkCore)
dotnet ef migrations add <Nombre>
# Aplicar migraciones + seed (OJO: desde la carpeta del proyecto — el host lee la
# configuración del directorio actual, no del .csproj):
cd src/IgasConsole.DbMigrator && dotnet run

# Levantar la API (Swagger en https://localhost:44338/swagger)
dotnet run --project src/IgasConsole.HttpApi.Host

# Instalar libs cliente (Angular/host) — requiere yarn con ignore-engines si node < 24
abp install-libs

# Seguridad local (ver skill `verify`)
dotnet list package --vulnerable --include-transitive
gitleaks detect --source .

# Simular el CI localmente antes de hacer push:
dotnet build IgasConsole.slnx -c Release && dotnet test IgasConsole.slnx -c Release

# ⚠️ Y comprobar que no hay codigo fuente que git IGNORA. Un archivo nuevo dentro de una
# carpeta que el .gitignore excluye compila en local —esta en el disco— y revienta el CI
# con "The name X does not exist". Paso de verdad con `Domain.Shared/Logs/LogConsts.cs`:
# el .gitignore trae `Logs/` a secas, para los archivos que escribe la aplicacion.
git ls-files --others --ignored --exclude-standard | grep -E '\.(cs|ts)$' | grep -v -E '/(bin|obj|node_modules)/'
```

> Sin Docker: la base de datos de dev y los tests usan un **PostgreSQL instalado en la
> máquina** (`localhost:5432`). Las credenciales nunca van en `appsettings*.json`:
> en dev van en user-secrets, y las de los tests en la variable de entorno
> `IGASCONSOLE_TEST_POSTGRES` (ver `.env.example`).
> No hay `docker-compose` ni stack de monitoreo en contenedores (ver
> `reglas-template-abp-sin-docker.md` de abt-template, apéndice de próximos pasos).

## 3. Estructura de carpetas

| Proyecto | Qué va aquí |
|---|---|
| `src/*.Domain.Shared` | Constantes, enums, textos compartidos (`ProductConsts`, `DataSourceNames`) |
| `src/*.Domain` | Entidades/agregados, interfaces de repositorio, interfaces de fuentes de datos, read models |
| `src/*.Application.Contracts` | DTOs, interfaces de application services, permisos |
| `src/*.Application` | Application services (orquestan y mapean a DTOs) |
| `src/*.EntityFrameworkCore` | **Infraestructura**: DbContext, repositorios, `SqlQueryRaw`, factory Dapper, `.sql` embebidos |
| `src/*.HttpApi(.Client)` | Controllers auto/manuales y proxies |
| `src/*.HttpApi.Host` | Host de la API (config, Swagger, health checks) |
| `src/*.DbMigrator` | Aplica migraciones y hace seed |
| `test/*.Architecture.Tests` | Reglas de arquitectura (NetArchTest) |
| `test/*.Integration.Tests` | Integración contra PostgreSQL + Respawn |

## 4. La regla de acceso a datos (SIN AMBIGÜEDAD)

- **Escritura y modelo de dominio → EF Core / LINQ** (repositorios de ABP). Nunca SQL crudo para escribir en la base principal.
- **Lectura compleja sobre la base principal → `dbContext.Database.SqlQueryRaw<TDto>(sql, {0}, {1}...)`** sobre el `DbContext` del Unit of Work (ver `ProductRepository`). Parámetros **por posición**.
- **Fuentes externas de solo lectura → Dapper** vía `IExternalDataSourceConnectionFactory` (ver `ExternalSalesReportReader`). Parámetros **nombrados**. Fuera del Unit of Work.
- **Prohibido concatenar variables en SQL.** El SQL vive en archivos `.sql` embebidos (`SqlResource.Load(...)`), versionados.

### 4.1 Paginar, filtrar y ordenar con SQL crudo

Ver `ProductRepository` + `Products/Sql/*.sql`. Cuatro cosas que no se ven leyendo por encima:

- ⚠️ **El `ORDER BY` no puede ser un parámetro** — un `DbParameter` es un valor, no sintaxis.
  Y `PagedAndSortedResultRequestDto.Sorting` es una **cadena libre que llega por la API**.
  Concatenarla es una inyección de manual. La solución es una **lista blanca**: la cadena del
  cliente solo sirve para *elegir* entre fragmentos literales escritos en el código; lo que
  no encaja cae al orden por defecto (no lanza: ordenar por algo inexistente no justifica
  romper la pantalla). Hay un test que manda `"Name; DROP TABLE ..."` y comprueba que la
  tabla sigue ahí.
- ⚠️ **Los `.sql` NO llevan `;` final.** `SqlQueryRaw` devuelve un `IQueryable`; en cuanto se
  compone algo encima (un `SingleAsync` añade `LIMIT`), EF **envuelve la consulta en una
  subconsulta** y el `;` de en medio da `42601: syntax error at or near ";"`. Con
  `ToListAsync` no se nota porque no compone nada — por eso el fallo aparece al añadir un
  conteo y no antes.
- **El total va en un `.sql` aparte**, no en un `COUNT(*) OVER()`: con la ventana, pedir una
  página más allá del final devuelve cero filas y con ellas ningún total, así que el cliente
  leería «no hay resultados» en vez de «te pasaste de página». El precio es que **el `WHERE`
  queda duplicado y puede desincronizarse sin dar error** — la paginación simplemente
  mentiría. Hay tests que comparan filas y total con cada filtro.
- **El orden por defecto lleva desempate** (`"StockCount" DESC, "Name" ASC`). Sin él,
  PostgreSQL no garantiza nada entre filas empatadas y al paginar una fila puede salir dos
  veces o ninguna.
- Un escalar (`SqlQueryRaw<long>`) exige que la columna se llame **`"Value"`**. Y un filtro
  vacío se manda como `NULL`, para que «sin filtro» no acabe significando «que contenga la
  cadena vacía».

## 5. Trampas detalladas (historial de "lo que no se ve leyendo el código")

Este `CLAUDE.md` se carga entero en cada sesión, así que el historial detallado de trampas
—con su causa raíz, cómo se encontró y qué test la vigila— vive aparte, en
`docs/trampas/`, para no inflar el contexto de cada conversación. **Léelos bajo demanda**
cuando el tema encaje con lo que estás tocando; el índice completo con una línea por archivo
está en [`docs/trampas/README.md`](docs/trampas/README.md):

- [nucleo-abp.md](docs/trampas/nucleo-abp.md) — transaccionalidad, filtros globales de EF,
  auditoría, DI por convención, `UseErrorPage()`, Angular fuera de zona, 2FA por Telegram.
- [jobs-avisos-backups.md](docs/trampas/jobs-avisos-backups.md) — background jobs, la
  campana de avisos, caducidad de claves de API, copias de seguridad.
- [archivos-api-keys-seguridad.md](docs/trampas/archivos-api-keys-seguridad.md) — BLOBs y
  foto de perfil, claves de API, bloqueo de IPs, registro y auditoría, export CSV, sesiones.
- [frontend-ui-general.md](docs/trampas/frontend-ui-general.md) — menú/tablas en móvil,
  traducción de pantallas.
- [multitenancy-y-marca.md](docs/trampas/multitenancy-y-marca.md) — heredado del template
  multi-tenant; aquí solo aplica la parte de marca.
- [modulos-features-y-tipos-usuario.md](docs/trampas/modulos-features-y-tipos-usuario.md) —
  heredado; features, cuotas y tipos de usuario se quitaron. Sigue valiendo lo de import CSV.
- [piel-accesibilidad-y-componentes.md](docs/trampas/piel-accesibilidad-y-componentes.md) —
  tema visual, accesibilidad WCAG, tests de front, componentes compartidos.
- [acceso-superadmin.md](docs/trampas/acceso-superadmin.md) — esconder la puerta del
  superadministrador.

## 6. Cómo agregar una fuente de datos nueva

Ver skill `datasource`. Resumen: constante en `DataSourceNames` → metadatos en
`appsettings:DataSources:{Nombre}` (sin credenciales) → cadena en
`ConnectionStrings:{Nombre}` (user-secrets en dev, variables de entorno en prod) →
usar `IExternalDataSourceConnectionFactory.CreateOpenConnectionAsync(DataSourceNames.X)`.

## 7. Observabilidad

- Health checks del template: `/health-status` (JSON) y `/health-ui`.
- **OpenObserve + OpenTelemetry** (docs/observabilidad.md): trazas, métricas por ruta y logs de la API,
  y los errores de JavaScript de la web y la app (vía `ErroresDelClienteAppService`). Autoalojado en el
  VPS (`/opt/openobserve`, `obs.ziacentro.mx`); sin `Observabilidad:Otlp:Endpoint` no se exporta nada.
- Vigilancia externa: workflow `vigilancia.yml` en el repo público Roque98/ZIA (Telegram).

### 7.1 Retención de logs: nada crecía sin techo, pero tampoco había ventana

Los logs viven en dos sitios y **ninguno de los dos tenía retención configurada**.

- **El archivo de la aplicación** (`/var/log/zia-console/api/logs.txt`, al que apunta el
  symlink `current/api/Logs`) era **un solo archivo que no rotaba nunca**: medido a los cuatro
  días de vida, **40 MB**, unos 10 MB/día. Ahora rota a diario con corte por tamaño y 30 días
  de retención (`LogConsts` + la sección `Serilog` de `appsettings.json`).
  - ⚠️ Los valores están **dos veces** y no hay forma de evitarlo: el logger de **arranque** se
    crea antes de que exista configuración que leer. Sin rotación ahí quedaría un `logs.txt`
    plano, sin rotar, junto a los que sí rotan.
  - El **DbMigrator** tiene su propio archivo y su propia configuración. Escribe poco, pero es
    quien guarda qué migración se aplicó y cuándo.
- **`journald`** ya guardaba en disco, pero con todo por defecto: límites **solo por tamaño**
  (10 % del sistema de archivos) y **ninguno por tiempo**. Eso no llena el disco; lo que hace
  es algo más tonto — la ventana de historial depende de cuánto ruido haya hecho la máquina, y
  no la sabes. Un día con una tormenta de errores se traga las semanas anteriores sin avisar.
  `deploy/journald-zia-console.conf` fija **30 días** y un tope de 3 GB, y lo instala
  `setup-servidor.sh`. Aplicado y verificado en el servidor con `systemd-analyze cat-config`.
- **Por qué 30 días y no más**: es lo que hace falta para responder «¿qué pasó hace tres
  semanas?». Lo que interesa a largo plazo —peticiones, cambios de entidades, inicios de
  sesión, fallos de trabajos— ya vive en la **base de datos** con su propia retención, entra en
  las copias y se consulta desde las pantallas. El archivo es para la traza suelta y la
  excepción con su pila.
- ⚠️ Al desplegar esto por primera vez, el `logs.txt` viejo (40 MB) **no lo limpia la
  retención**: no encaja con el patrón de los rotados, así que se queda ahí para siempre. Hay
  que borrarlo a mano una vez.

## 8. Prohibiciones explícitas

- ❌ No usar módulos/paquetes **Pro** de ABP ni tema LeptonX Pro (solo tier open source).
- ❌ No concatenar variables en SQL (usa parámetros por posición o nombrados).
- ❌ No poner secretos en `appsettings*.json` (usa user-secrets / variables de entorno). Los pass-phrases que traía la plantilla ya están vacíos en `HttpApi.Host/appsettings.json`; los valores reales viven en user-secrets (dev) y en `/etc/zia-console/env` (producción). El CI corre `gitleaks` sobre todo el historial.
- ❌ No abrir conexiones de base de datos fuera de `IExternalDataSourceConnectionFactory` (lo verifica un test de arquitectura).
- ❌ No ejecutar un deploy real a producción sin confirmación explícita del usuario.

## 9. Regla Cero — No inventes APIs de ABP

ABP está poco representado en los datos de entrenamiento. **Antes de usar un tipo,
método o paquete de ABP, verifícalo en `refs/abp`** (o en la doc oficial). Si no lo
encuentras, dilo en tu reporte en vez de proponer una API plausible. Tras cada
cambio significativo corre `dotnet build` (un hook lo hace tras editar `.cs`).

## 10. Feature de referencia — EL PATRÓN A COPIAR

`Organizaciones` es la feature completa de referencia (el módulo de Análisis se quitó). Cópiala
para nuevas features:

- Entidad (agregado con hijas): `src/*.Domain/Organizaciones/Organizacion.cs` (+ constantes en
  `Domain.Shared/Organizaciones`)
- Mapeo EF: `IgasConsoleDbContext` (bloque «Organizaciones»)
- Contratos y DTOs: `src/*.Application.Contracts/Organizaciones/`
- Application service (permisos, jerarquía, 404 para lo ajeno):
  `src/*.Application/Organizaciones/OrganizacionAppService.cs`
- Tests de integración: `test/*.Integration.Tests/Organizaciones/OrganizacionIntegrationTests.cs`
- Pantalla Angular: `angular/src/app/organizaciones/`

El ejemplo de **lectura con SQL crudo** (`SqlQueryRaw` + `.sql` embebidos, paginación con
lista blanca de orden) y de **fuente externa con Dapper** era el módulo de productos, que se
quitó de Igas. El código completo sigue en abt-template
(`D:/proyectos/perso/abt-template`: `ProductRepository`, `Products/Sql/*.sql`,
`ExternalSalesReportReader`); la infraestructura (`SqlResource`,
`IExternalDataSourceConnectionFactory`) sigue aquí.

## 11. Explorador de BD (solo Development)

Herramienta de administración para navegar y editar la base principal desde la UI
(`/db-explorer` en Angular; app service `IDbExplorerAppService`).

- **Solo Development**: el Host activa `DbExplorerOptions.IsEnabled` con `IsDevelopment()`;
  fuera de dev el endpoint y el menú no existen.
- **Permiso**: `IgasConsole.DbExplorer` (ver) y `IgasConsole.DbExplorer.Edit` (editar). Solo el
  Developer (rol `admin`), al que ABP concede todos los permisos.
- **Seguridad**: lee todas las tablas del esquema `public` (excluye `pg_catalog` e
  `information_schema`); **solo edita tablas de la app** (prefijo `App`). El
  SQL dinámico valida los identificadores contra el esquema vivo y parametriza los valores
  (anti-inyección). El SQL de introspección vive en `DbExplorer/Sql/*.sql`.
- ⚠️ El CRUD directo **salta la validación de dominio, la auditoría y los filtros de ABP**.
  Es una herramienta de dev, no un sustituto de los application services.
