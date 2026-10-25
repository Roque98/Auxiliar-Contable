# CLAUDE.md — Auxiliar contable

> Consola para un **despacho contable con varios clientes**: **ABP Framework (tier open source)**
> + Angular, .NET 10, SQL explícito versionado y **sin Docker** (base de datos y tests con
> **PostgreSQL** local). Nació en octubre de 2026 como copia recortada de **ZIA-Console**
> (Roque98/ZIA-Console, que a su vez venía de `abt-template`), sin multi-tenancy. Es un
> proyecto **aparte**: con ZIA solo comparte el VPS y el dominio ziacentro.mx (subdominios
> propios); no se toca nada de ZIA desde aquí.
> La solución vive en `AuxiliarContable/`. El código fuente de ABP está clonado en
> `refs/abp` como fuente de verdad para verificar APIs (ver "Regla Cero").

## 1. Qué es el proyecto

Solución ABP en capas (DDD): `Domain(.Shared)` → `Application(.Contracts)` →
`EntityFrameworkCore` (infraestructura) → `HttpApi(.Host)`. **Una sola organización**
(`MultiTenancyConsts.IsEnabled = false`; el módulo de tenants de ABP sigue instalado pero
inactivo), con Identity + OpenIddict + Angular UI. La lectura compleja usa **SQL crudo
versionado**; la escritura y el modelo de dominio usan **EF Core / LINQ**.

### 1.0 El producto (lo que pidió el usuario)

> ⚠️ **Lee primero [`docs/vision-producto.md`](docs/vision-producto.md)**: la visión completa
> (el chat como interfaz principal, perfil fiscal con CSF, bóveda, el chat del equipo) y las
> **reglas de trabajo** (ser proactivo, cuándo decidir y cuándo preguntar, el resumen al final
> de cada sesión, principios de IA no negociables). Manda sobre este resumen. El plan vigente y
> las decisiones pendientes están en [`docs/plan/`](docs/plan/).

- Un despacho contable lleva **varios clientes** (cada uno, una organización; ver 1.2.6).
- El cliente manda: **estados de cuenta** en cualquier formato (PDF, imágenes, fotos y lo que
  tenga), **facturas emitidas y recibidas** (CFDI) e información adicional para el **perfil del
  cliente**. Llegan por un **portal en la consola** (el cliente sube lo suyo) o **los sube el
  contador**.
- **Primer objetivo**: organizar y almacenar la información de cada cliente para **conciliar
  sus facturas con su información interna** (estados de cuenta).
- Decisiones ya tomadas para la IA:
  - CFDI (XML) se lee **con código**, sin IA.
  - Estados de cuenta: un LLM con visión y salida **JSON de esquema fijo**; se valida por
    código que saldo inicial + abonos − cargos = saldo final. Si no cuadra, **revisión humana**.
  - Conciliación primero **por reglas** (monto, fecha ±N días, RFC/UUID); la IA solo para lo
    dudoso.
- Producción: `contable.ziacentro.mx` (web) y `api-contable.ziacentro.mx` (API) en el VPS de
  ZIA. Repo: `Roque98/Auxiliar-Contable` (privado).

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
  `AuxiliarContableModuleExtensionConfigurator`: la pantalla de Roles de ABP la enseña sola), no se
  deduce del nombre. `NivelesDeRoles` (caché) da el mapa nombre → nivel; el seed lo pone a los
  cuatro propios. Crear/editar un rol solo con nivel por debajo del propio; los propios no
  cambian de nivel; el de Developer es solo de `admin`. ⚠️ `JerarquiaDeUsuarios.NivelActualAsync`
  (ya no hay versión síncrona).
- La regla vive en `Domain.Shared/Identity/AuxiliarRoles.cs` (lógica pura) y
  `Domain/Identity/JerarquiaDeUsuarios.cs`. La aplican los tres servicios que sustituyen a
  los de ABP en `Application/Identity/`: usuarios, roles y permisos. **Cualquier endpoint
  nuevo que liste o toque usuarios tiene que pasar por la jerarquía**; si no, el Owner
  vería al Developer por ahí.
- Los permisos técnicos (métricas, trabajos, seguridad, auditoría, explorador de BD) son
  solo del Developer: además, casi todos enseñan actividad por usuario, incluida la suya.
- `AuxiliarRolesDataSeedContributor` crea los roles `IsStatic` y les da sus permisos
  (`PermisosInicialesPorRol`) **solo al crearlos**: el migrador corre en cada deploy y
  re-sembrar devolvería permisos que el Owner quitó.
- Los permisos de ABP que no aplican (inquilinos, features y ajustes de SMTP/zona horaria)
  están **apagados** en `AuxiliarContablePermissionDefinitionProvider.PostDefine`: no salen en
  la pantalla de permisos ni se conceden a nadie. «Configuraciones» queda oculta en el menú
  porque se quedó sin pestañas.
- Tests: `test/*.Integration.Tests/Identity/JerarquiaIntegrationTests.cs`.

### 1.2 Brevo (correo, SMS, WhatsApp)

- Correo: `BrevoEmailSender` sustituye el `IEmailSender` de ABP (en el host), así que todo
  lo de ABP que manda correo sale por Brevo. SMS/WhatsApp: `IMensajeria` (dominio) →
  `BrevoMensajeria` (host). Cliente HTTP: `HttpApi.Host/Notifications/Brevo/BrevoClient.cs`.
- Sin `Brevo:ApiKey` no se envía nada y queda un aviso en el log (dev y tests).
- La API key va en user-secrets (dev) / `/etc/auxiliar-contable/env` (prod). Si la cuenta
  tiene «Authorised IPs», Brevo responde 401 a IPs no listadas.
- `IEstadoDelCorreo` dice si hay correo de verdad (API key **y** `Brevo:SenderEmail`). Sin
  eso las invitaciones se rechazan y la pantalla lo avisa. Ojo: las implementaciones
  llevan `[ExposeServices]` explícito — el nombre no sigue la convención de ABP.
- **Plantillas de correo** (Scriban, `Domain.Shared/Emails/*.tpl`, registradas en
  `Domain/Emails/AuxiliarEmailTemplates.cs`): el layout **sustituye** al de ABP
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
- Idioma: solo español (`AuxiliarContableDomainSharedModule` + `DefaultLanguage = "es"`).

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

### 1.2.4 Pruebas de pantalla (`AuxiliarContable/e2e/`, Playwright)

Desbordes y pestañas escondidas a **390 / 768 / 1024 / 1280 px** (teléfono, tablet vertical,
tablet horizontal, escritorio), contraste en modo oscuro, fundido del tema, Usuarios,
Seguridad, Ayuda y registro inválido, contra la app LOCAL arrancada (ver `e2e/README.md`).

- **Toda pantalla nueva** entra en `RUTAS` (`e2e/pruebas/ayudas.ts`) y se revisa además a ojo
  (capturas) en teléfono y tablet, en claro y oscuro, antes de darla por terminada.
- Menú desplegable hasta **900 px** (el corte está en el `.ts` y el `.scss` de `AuxiliarLayout`):
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

### 1.2.6 Organizaciones — los clientes del despacho y QUIÉN VE QUÉ

`/organizaciones` (permiso `AuxiliarContable.Organizaciones`): un cliente del despacho = una
organización con **miembros** (usuarios). Toda la información de un cliente (estados de cuenta,
facturas, perfil) colgará de su organización.

- Lo del **Developer** no existe para nadie más (`DevelopersOcultosAsync`). Para listar PERSONAS
  (miembros, candidatos) sigue `UsuariosFueraDeAlcanceAsync` y añadir/quitar exige
  `ExigirGestionableAsync` (404).
- ⚠️ Un `Guid? xxxId` suelto en un app service de ABP va a la RUTA (`/personas/{organizacionId}`)
  y choca con `{id}`: se pasa en un DTO de entrada.
- **Alta dentro de una organización**: un cliente (nivel Usuario) no se crea sin ella
  (`MembresiasDeOrganizacion`). A mano, llega en `ExtraProperties["OrganizacionId"]` del DTO
  de ABP y lo aplica `AuxiliarIdentityUserAppService` (también al editar un cliente sin
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
- **Autorizador** (`Application/Autorizacion`): el ÚNICO punto que decide quién ve qué de cada
  organización (`QuienMira`: equipo, Developer y membresías con papel). Todo módulo nuevo que
  guarde información de un cliente pregunta aquí. Se calcula en cada llamada: ⚠️ no guardarlo
  en la instancia (un servicio que vive más que una petición vería membresías viejas; pasó en
  los tests).
- ⚠️ Los miembros son hijos del agregado con **clave compuesta**: guardar con
  `CurrentUnitOfWork.SaveChangesAsync()`, NO `UpdateAsync` — `Update` los marca como
  modificados y los nuevos nunca se insertan.
- ⚠️ En los tests la autorización es «todo permitido» (`AddAlwaysAllowAuthorization`): un
  `[Authorize]` nunca lanza. `AlcanceSegunRol` (módulo de tests) sustituye a
  `AlcanceDeOrganizaciones` para que el rol `usuario` haga de cliente; lo que dependa de un
  permiso se prueba comprobando el atributo y `PermisosInicialesPorRol`
  (`Un_cliente_no_gestiona_organizaciones`).
- Permisos nuevos a roles que ya existen: `PermisosNuevosPorVersion` (se aplica una vez por
  versión, apuntada en el ajuste `AuxiliarContable.Permisos.VersionSembrada`).
- Tests: `test/*.Integration.Tests/Organizaciones/`.

### 1.2.7 Actividad — quién hizo qué

`/actividad` (permiso `AuxiliarContable.Actividad`, Owner y Administrador): `ActividadAppService`
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

### 1.2.8 IA (Claude Haiku) — leer documentos con esquema fijo

- Contrato en el dominio: `IModeloDeIA.ExtraerAsync<T>(SolicitudDeIA)` (`Domain/IA/`). Una
  solicitud = propósito (etiqueta fija para logs/métricas) + instrucciones (system prompt) +
  `EsquemaDeSalida` (JSON Schema, `"type": "object"`) + partes de contenido (texto, imagen,
  PDF). Devuelve `T` deserializado en camelCase y el uso de tokens. **No hay chat ni texto
  libre**: todo lo que sale se valida por código antes de guardarse (§1.0).
- Implementación: `HttpApi.Host/IA/Claude/ClaudeModeloDeIA.cs`, Messages API con HttpClient (como
  Brevo, sin SDK). La salida estructurada se fuerza con UNA herramienta cuyo `input_schema` es el
  esquema y `tool_choice` obligatorio. Las instrucciones van con `cache_control` (caché de
  prompts: abarata si son largas y se repiten).
- Configuración `Claude:*` (appsettings sin secretos); `Claude__ApiKey` en user-secrets (dev) /
  `/etc/auxiliar-contable/env` (prod). Modelo por defecto `claude-haiku-5-5`. Sin clave,
  `EstaConfigurado = false` y cada uso lanza sin llamar (DbMigrator y tests:
  `ModeloDeIASinConfigurar`).
- Errores: `ErrorDeIAException` con `Motivo`. Solo `NoDisponible` (429, 5xx, 529, timeout) es
  `EsReintentable`; el cliente ya reintenta 3 veces respetando `retry-after`. `Truncada`
  (`stop_reason: max_tokens`) y `RespuestaInvalida` NO se dan por buenas: van a revisión humana
  o se sube `MaxTokens`. El timeout NO se reintenta (con un PDF grande pasaría otra vez y se
  pagaría doble).
- Límites comprobados antes de llamar (`IAConsts`): imagen jpeg/png/gif/webp ≤ 5 MB (HEIC de
  iPhone NO: convertir antes), petición ≤ 32 MB **en base64** (un PDF de 25 MB ya no cabe).
- ⚠️ **Nada del contenido ni de la respuesta va al log ni a los mensajes de error**: son datos
  financieros de los clientes. Solo propósito, modelo, tokens, duración y `request-id`.
- ⚠️ Cada llamada cuesta dinero y tarda segundos: **solo desde trabajos en segundo plano**, nunca
  dentro de la petición del usuario.
- Métricas (OpenObserve, medidor `AuxiliarContable.IA`): `auxiliar.ia.tokens` por propósito,
  modelo y tipo (entrada/salida/caché), `auxiliar.ia.llamadas` por resultado y
  `auxiliar.ia.duracion`.
- Tests: `test/*.HttpApi.Host.Tests/IA/` (API falsa; nunca se llama a Anthropic) y
  `test/*.Domain.Tests/IA/` (validaciones).

### 1.3 Design system (el aspecto de TODO el front)

El design system viene tal cual de ZIA-Console; solo cambian la marca y el acento.

**Marca**: el isotipo es una hoja de registro con la palomita de «conciliado»
(`docs/marca/isotipo.svg`; cómo sacar los PNG en `docs/marca/README.md`). El nombre va en
**texto** (el que el Owner ponga en «Marca»), no en un logotipo dibujado. Acento: azul marino
`#2c5282` (8:1 con texto blanco); en el tema oscuro se aclara a `#3b6fb6` (3,5:1 sobre el
fondo, 5,1:1 con texto blanco). ⚠️ El acento de serie NO se escribe en línea sobre <html>
(`COLOR_DE_SERIE` en `branding.service.ts`): si se escribiera, taparía el tono del tema oscuro.
Un acento elegido en «Marca» sí se escribe y manda en los dos temas.

- Se queda a propósito el prefijo `ig-` / `--ig-` de clases y tokens del design system:
  renombrarlo no aporta nada.

- Tokens, tipografía (Sora, servida local) y componentes: `angular/src/styles/auxiliar/`.
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

Todos se ejecutan desde `AuxiliarContable/` (la carpeta de la solución):

```bash
# Build (con TreatWarningsAsErrors — un warning rompe el build)
dotnet build AuxiliarContable.slnx -c Debug

# Tests
dotnet test AuxiliarContable.slnx                                             # toda la suite
dotnet test test/AuxiliarContable.Architecture.Tests/*.csproj                 # arquitectura
dotnet test test/AuxiliarContable.Integration.Tests/*.csproj                  # integración (PostgreSQL + Respawn)
dotnet test test/AuxiliarContable.EntityFrameworkCore.Tests/*.csproj          # unit/EF (SQLite en memoria)

# Migraciones (desde src/AuxiliarContable.EntityFrameworkCore)
dotnet ef migrations add <Nombre>
# Aplicar migraciones + seed (OJO: desde la carpeta del proyecto — el host lee la
# configuración del directorio actual, no del .csproj):
cd src/AuxiliarContable.DbMigrator && dotnet run

# Levantar la API (Swagger en https://localhost:44338/swagger)
dotnet run --project src/AuxiliarContable.HttpApi.Host

# Instalar libs cliente (Angular/host) — requiere yarn con ignore-engines si node < 24
abp install-libs

# Seguridad local (ver skill `verify`)
dotnet list package --vulnerable --include-transitive
gitleaks detect --source .

# Simular el CI localmente antes de hacer push:
dotnet build AuxiliarContable.slnx -c Release && dotnet test AuxiliarContable.slnx -c Release

# ⚠️ Y comprobar que no hay codigo fuente que git IGNORA. Un archivo nuevo dentro de una
# carpeta que el .gitignore excluye compila en local —esta en el disco— y revienta el CI
# con "The name X does not exist". Paso de verdad con `Domain.Shared/Logs/LogConsts.cs`:
# el .gitignore trae `Logs/` a secas, para los archivos que escribe la aplicacion.
git ls-files --others --ignored --exclude-standard | grep -E '\.(cs|ts)$' | grep -v -E '/(bin|obj|node_modules)/'
```

> Sin Docker: la base de datos de dev y los tests usan un **PostgreSQL instalado en la
> máquina** (`localhost:5432`). Las credenciales nunca van en `appsettings*.json`:
> en dev van en user-secrets, y las de los tests en la variable de entorno
> `AUXILIARCONTABLE_TEST_POSTGRES` (ver `.env.example`).
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
  VPS y compartido con ZIA (`/opt/openobserve`, `obs.ziacentro.mx`; lo mantiene ZIA-Console); sin `Observabilidad:Otlp:Endpoint` no se exporta nada.
- Vigilancia externa: la de ZIA (workflow `vigilancia.yml` en Roque98/ZIA) NO mira esta app;
  añadir `contable.ziacentro.mx` ahí o montar una propia está pendiente.

### 7.1 Retención de logs: nada crecía sin techo, pero tampoco había ventana

Los logs viven en dos sitios y **ninguno de los dos tenía retención configurada**.

- **El archivo de la aplicación** (`/var/log/auxiliar-contable/api/logs.txt`, al que apunta el
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
  `deploy/journald-auxiliar-contable.conf` fija **30 días** y un tope de 3 GB, y lo instala
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
- ❌ No poner secretos en `appsettings*.json` (usa user-secrets / variables de entorno). Los pass-phrases que traía la plantilla ya están vacíos en `HttpApi.Host/appsettings.json`; los valores reales viven en user-secrets (dev) y en `/etc/auxiliar-contable/env` (producción). El CI corre `gitleaks` sobre todo el historial.
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
- Mapeo EF: `AuxiliarContableDbContext` (bloque «Organizaciones»)
- Contratos y DTOs: `src/*.Application.Contracts/Organizaciones/`
- Application service (permisos, jerarquía, 404 para lo ajeno):
  `src/*.Application/Organizaciones/OrganizacionAppService.cs`
- Tests de integración: `test/*.Integration.Tests/Organizaciones/OrganizacionIntegrationTests.cs`
- Pantalla Angular: `angular/src/app/organizaciones/`

El ejemplo de **lectura con SQL crudo** (`SqlQueryRaw` + `.sql` embebidos, paginación con
lista blanca de orden) y de **fuente externa con Dapper** era el módulo de productos, que se
quitó de la consola. El código completo sigue en abt-template
(repo `abt-template`: `ProductRepository`, `Products/Sql/*.sql`,
`ExternalSalesReportReader`); la infraestructura (`SqlResource`,
`IExternalDataSourceConnectionFactory`) sigue aquí.

## 11. Explorador de BD (solo Development)

Herramienta de administración para navegar y editar la base principal desde la UI
(`/db-explorer` en Angular; app service `IDbExplorerAppService`).

- **Solo Development**: el Host activa `DbExplorerOptions.IsEnabled` con `IsDevelopment()`;
  fuera de dev el endpoint y el menú no existen.
- **Permiso**: `AuxiliarContable.DbExplorer` (ver) y `AuxiliarContable.DbExplorer.Edit` (editar). Solo el
  Developer (rol `admin`), al que ABP concede todos los permisos.
- **Seguridad**: lee todas las tablas del esquema `public` (excluye `pg_catalog` e
  `information_schema`); **solo edita tablas de la app** (prefijo `App`). El
  SQL dinámico valida los identificadores contra el esquema vivo y parametriza los valores
  (anti-inyección). El SQL de introspección vive en `DbExplorer/Sql/*.sql`.
- ⚠️ El CRUD directo **salta la validación de dominio, la auditoría y los filtros de ABP**.
  Es una herramienta de dev, no un sustituto de los application services.
