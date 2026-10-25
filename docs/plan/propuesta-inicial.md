# Propuesta inicial: estado, dudas, riesgos, arquitectura y fases

> 2026-10-25. Respuesta a la «primera tarea» de [`../vision-producto.md`](../vision-producto.md).
> Sin código de features. Las decisiones marcadas **[DECIDES TÚ]** esperan respuesta; las
> marcadas **[TOMADA]** las tomé yo por ser reversibles y baratas.

## 0. Decisiones tomadas (2026-10-25)

| Tema | Decisión | Quién |
|---|---|---|
| Despachos | **Uno solo.** Sin multi-tenancy de ABP; los clientes son organizaciones | Dueño |
| Cliente | **1 organización = 1 RFC**; quien tenga dos RFC son dos clientes | Dueño |
| Alcance del equipo | **Owner y administradores ven todos los clientes** (como ya funciona); un cliente pertenece a un solo despacho. No hay asignación por administrador | Dueño |
| FKE | Ubicación desconocida por ahora: no se reimplementa lógica fiscal hasta encontrarlo | Dueño |
| Documentos | **En el disco del VPS**, cifrados por la aplicación, con copia externa | Dueño |
| Antivirus | **ClamAV en el VPS**, se instala con la fase 1 (cuando haya subidas): hoy no escanearía nada y ocuparía ~1 GB de RAM | Dueño + yo |
| Sesión de Claude en el VPS | `claude-cuenta2b` detenida y deshabilitada (libera memoria para lo anterior) | Dueño |
| Despliegue | Usuario propio `deploy-auxiliar` (no el `deploy` compartido con ZIA) y script de activación de root fuera de su alcance | Yo |
| Aviso de privacidad | Página pública `/privacidad`, casilla obligatoria al registrarse y panel para quien no lo aceptó; constancia por versión | Yo (texto: borrador para abogado) |

Pendiente: el modelo del chat (§4.2) y los datos del despacho para el aviso.

## 1. Estado actual del stack

**Lo que ya hay y sirve tal cual**

| Pieza | Estado |
|---|---|
| Backend | ABP 10.6 open source, .NET 10, DDD en capas, PostgreSQL 16, sin Docker. Tests: 394 (unidad, arquitectura, integración contra PostgreSQL real) |
| Front | Angular 22 + LeptonX con design system propio (`ig-`), modo oscuro, accesible (WCAG), Playwright e2e |
| Identidad | Login propio, 2FA (correo/Telegram), códigos de recuperación, invitaciones por correo, sesiones, bloqueo de IPs, límites de peticiones |
| Roles | Developer → Owner → Administrador → Usuario, **ya con jerarquía** (cada uno ve solo lo de abajo; lo ajeno responde 404) |
| Clientes | `Organizaciones` con miembros y papel dentro (Administrador / Miembro / Lector). Es la unidad de aislamiento natural |
| Auditoría | Registro de llamadas de ABP (30 días), registro de seguridad, pantalla de Actividad «quién hizo qué» |
| IA | `IModeloDeIA` (interfaz propia) + Claude Haiku por la Messages API: extracción con esquema JSON fijo, texto/imagen/PDF, reintentos, métricas de tokens. Probado contra la API real |
| Correo | Brevo (correo; SMS apagado) |
| Observabilidad | OpenTelemetry → OpenObserve compartido con ZIA, health checks |
| Producción | VPS de ZIA (4 CPU, 8 GB RAM, 41 GB libres), nginx + certificado propio, systemd, copia diaria de la base |

**Lo que no hay:** nada de documentos, perfil fiscal, CFDI, estados de cuenta, chat ni bóveda.
Los archivos hoy se guardan **dentro de PostgreSQL** (BLOB storage de ABP), pensado para fotos de
perfil.

## 2. Dudas abiertas, por prioridad

Las tres primeras cambian el modelo de datos: necesito la respuesta antes de escribir código.

1. **[DECIDES TÚ] ¿Un despacho o muchos?** Dices «multi-tenant», pero hoy la app es de **un solo
   despacho** (el Owner) con muchos clientes (organizaciones). ¿Algún día habrá varios despachos,
   cada uno con su Owner, sus admins y sus clientes, sin verse entre sí? Si sí, conviene saberlo
   ya: activar el multi-tenancy de ABP después cuesta mucho más que hoy.
2. **¿Dónde está el FKE y en qué forma?** ¿Repo (cuál), paquete NuGet, carpeta? ¿.NET 10 o
   anterior? ¿Tiene tests? ¿Es puro, sin base de datos ni red, o depende de algo? De eso depende si
   entra como referencia de proyecto, como paquete o como servicio aparte.
3. **El cliente, ¿es una persona o una empresa con varias personas?** Hoy una organización puede
   tener varios miembros con papeles distintos. ¿El perfil fiscal es de la **organización** (un
   RFC por organización)? ¿Puede un despacho llevar a un cliente con **varios RFC** (persona
   física + su empresa)? Mi propuesta: **1 organización = 1 RFC**, y si alguien tiene dos, son dos
   organizaciones.
4. **Asignación de administradores.** Hoy un Administrador ve **todos** los clientes. Tu visión
   dice «solo los asignados». ¿Un cliente puede tener varios admins asignados? ¿El Owner ve todo?
   (Propuesta: varios admins por cliente; el Owner ve todo.)
5. **¿Quién paga la IA y cómo se controla?** ¿Hay un tope de gasto por cliente o por mes? ¿Se le
   cobra al cliente final? Define si hace falta medir el consumo **por organización** desde el día
   uno (yo lo mediría de todos modos).
6. **Volumen esperado** en 12 meses: número de clientes, documentos/mes por cliente, peso típico.
   Con 50 clientes × 200 documentos/mes el almacenamiento y el costo de IA son triviales; con 2,000
   clientes no.
7. **¿Hay contador humano que firme?** El disclaimer dice que la IA no sustituye la firma. ¿Las
   declaraciones las presenta el despacho (y la app solo prepara) o el cliente por su cuenta? Esto
   cambia qué «cierre de periodo» necesita la app.
8. **Descarga masiva del SAT.** ¿Te interesa que la app baje los CFDI directo del SAT (con la
   e.firma del cliente) en vez de que el cliente los suba? Es la mayor fuente de completitud, pero
   implica custodiar la e.firma: decisión delicada.
9. **App móvil.** Las fotos de tickets vienen del celular. ¿Basta con la web responsiva (subir
   foto desde el navegador del teléfono) o se espera app?
10. **WhatsApp como canal.** Para «platicar con tu contador», WhatsApp es donde está el cliente.
    No es fase 1, pero si lo quieres, el diseño del chat debe ser independiente del canal desde ya.

## 3. Riesgos que ya veo

**Altos (antes de meter datos reales de clientes)**

- **Las copias de seguridad solo viven en el VPS.** Si el disco muere, se pierde la contabilidad
  de los clientes. Hace falta una copia fuera (cifrada) **antes** del primer documento real.
- **Envío de datos fiscales a un proveedor extranjero (Anthropic, EE. UU.).** La LFPDPPP lo
  permite con aviso de privacidad y consentimiento: hace falta un **aviso de privacidad** que lo
  diga y aceptarlo al registrarse. según su política comercial Anthropic no entrena con datos de la API
  y los retiene por tiempo limitado (verificar la política vigente al redactar el aviso); conviene documentarlo y minimizar lo que se manda (p. ej. no mandar la CSF entera si el
  parseo determinista basta).
- **Aislamiento entre clientes en el chat.** El riesgo número uno de un asistente con
  herramientas es que el modelo pida datos de otro cliente. La defensa no puede estar en el
  prompt: cada herramienta debe ejecutarse **con los permisos y el alcance del usuario que
  conversa**, reusando los mismos servicios (y los mismos tests de aislamiento) que las
  pantallas. Ver §4.3.
- **Cifrado en reposo.** PostgreSQL no cifra por sí solo y el disco del VPS no está cifrado.
  Propuesta: cifrar los documentos a nivel de aplicación (AES-GCM, clave fuera de la base) y que
  la copia externa vaya cifrada.

**Medios**

- **Memoria del VPS.** 8 GB compartidos con ZIA y varias sesiones de Claude; ya hay swap en uso.
  OCR local (Tesseract) y antivirus (ClamAV, ~1 GB de RAM) compiten por esa memoria. Mi
  propuesta: no hacer OCR local (el modelo lee imágenes y PDF escaneados) y decidir el antivirus
  con datos (§4.6).
- **Documentos en PostgreSQL.** Con el volumen de §2.6 la base y su copia crecen rápido. Hay que
  decidirlo antes de la bóveda (§4.4).
- **Haiku para conversar.** Haiku es excelente para extraer con esquema, pero un orquestador que
  encadena herramientas en una conversación larga es la parte más exigente. Hay que medirlo con
  casos reales antes de comprometerse (§4.2).
- **Dependencias con vulnerabilidades conocidas** heredadas de ZIA (`System.Security.Cryptography.Pkcs`
  9.0.4, alta; `MessagePack` 3.1.7, moderada). El repo tiene `NuGetAudit` apagado, por eso no se
  ven al compilar.
- **El despliegue es manual** (falta la llave de despliegue de GitHub) y **no hay vigilancia
  externa** de la app.

**Bajos pero conviene saberlo**

- **CSF «reciente».** La CSF trae fecha de emisión y un QR que apunta a la validación del SAT; se
  puede comprobar la fecha sin IA. Verificarla contra el SAT es frágil (scraping), no la
  prometería.
- **CFDI cancelados.** Un XML válido puede estar cancelado. El SAT tiene un servicio público de
  consulta de estatus por UUID: proponerlo como validación (es exactamente el tipo de regla que
  debería vivir en el FKE).
- **Estados de cuenta en Excel/CSV.** Cada banco exporta distinto; un parser por banco no escala.
  El modelo puede **detectar el mapeo de columnas una vez por banco y formato**, y el código
  aplica ese mapeo (barato, determinista y cacheable).

## 4. Propuesta de arquitectura

### 4.1 Principio rector
El LLM **no toca la base de datos ni decide nada fiscal**. Orquesta la conversación y llama
herramientas; cada herramienta es código determinista que pasa por los mismos servicios de
aplicación, permisos y alcance que las pantallas. Si una pantalla no puede hacer algo, el chat
tampoco.

### 4.2 Capa de IA (desacoplada del proveedor)
- Ya existe `IModeloDeIA.ExtraerAsync<T>` (extracción con esquema).
- Se añade `IModeloConversacional`: mensajes + herramientas → respuesta en streaming con llamadas
  a herramientas. Implementación Claude; mañana otra (on-premise, otra nube) sin tocar el resto.
- **[DECIDES TÚ] Modelo del chat.** Opciones:
  - **A. Haiku para todo.** Lo más barato (varias veces más barato que Sonnet). Riesgo: peor criterio al encadenar
    herramientas y al conversar.
  - **B. Haiku para extraer y clasificar, Sonnet para el chat.** **Mi recomendación.** El chat es
    pocas llamadas caras y la extracción muchas baratas.
  - **C. Empezar con Haiku y medir.** Banco de pruebas con 30-50 conversaciones reales anotadas;
    se sube a Sonnet solo si falla. Barato de probar porque el modelo es configuración.
  Las tres funcionan con el mismo código; lo que decides es costo contra calidad.
- Consumo **por organización, propósito y modelo** registrado en la base (no solo métricas), para
  topes y para cobrar si algún día se cobra.

### 4.3 El chat y sus herramientas
- Entidades: `Conversacion` (dueño, **organización activa explícita**, canal), `Mensaje`
  (rol, contenido, herramientas llamadas con sus argumentos y resultados).
- **La organización activa es un dato de la conversación, no algo que el modelo «recuerde».**
  La UI la muestra siempre; cambiarla es un acto explícito del usuario (o una herramienta
  `cambiar_cliente` que la UI confirma). Las herramientas leen la organización **de la
  conversación**, nunca de sus argumentos.
- Registro de herramientas: cada una declara nombre, descripción, esquema de entrada, **permiso
  RBAC requerido** y **alcance** (cliente activo / cartera). Al iniciar cada turno se le ofrecen
  al modelo solo las que el usuario puede usar. Al ejecutar, se vuelve a comprobar todo (no se
  confía en que el modelo solo pida lo ofrecido).
- Toda ejecución de herramienta queda en la bitácora (quién, en nombre de qué cliente, qué
  argumentos, qué resultado).
- Respuestas con **citas**: cada cifra lleva el id del dato/documento de donde sale y la UI la
  vuelve clicable. Un test de «nunca inventar cifras»: si la respuesta trae un número que no salió
  de ninguna herramienta, se marca.
- Streaming por SSE (nginx ya está preparado para conexiones largas).

### 4.4 Documentos y bóveda
- `Documento`: organización, tipo, periodo, nombre original, tamaño, **SHA-256** (duplicados
  exactos), estado del proceso, origen (cliente/admin/chat), quién lo subió.
- Proceso por etapas en trabajos en segundo plano, cada una idempotente y reintentable:
  **recibido → identificado → extraído → validado → clasificación propuesta → confirmado**.
  Cada extracción se guarda versionada con su **método** (determinista / modelo X) para poder
  re-extraer con un modelo mejor sin perder la historia.
- Determinista primero: CFDI XML (parser propio + validaciones del FKE), CSF (texto del PDF),
  Excel/CSV (mapeo de columnas). Modelo solo para PDF escaneados, fotos y lo que el código no
  entienda.
- Trazabilidad: `Dato` → `Evidencia` (documento, página, fragmento) → `Clasificacion`
  (propuesta/confirmada, por quién, cuándo).
- **[DECIDES TÚ] Dónde viven los archivos.** Opciones:
  - **A. PostgreSQL (como ahora).** Cero infraestructura nueva, entra en la copia. Crece mal:
    base y copias pesadas.
  - **B. Disco del VPS**, cifrado por la app, con copia externa. **Mi recomendación para empezar:**
    simple, barato y suficiente para cientos de clientes. Se migra a C cambiando el proveedor de
    BLOB de ABP.
  - **C. Almacenamiento de objetos** (Cloudflare R2 / Backblaze B2, compatibles con S3). Escala y
    durabilidad, URLs firmadas nativas; costo bajo pero una dependencia y una cuenta más.
- Descargas siempre por la API autenticada con enlaces firmados de vida corta; nunca un archivo
  público.

### 4.5 Búsqueda
- **[TOMADA]** Fase 1 sin embeddings: los datos están estructurados (RFC, monto, fecha, tipo),
  y «enséñame las facturas de gasolina de agosto» es el modelo traduciendo a filtros de una
  herramienta de búsqueda. Más el texto completo de PostgreSQL (configuración `spanish`). Es más
  barato, exacto y el aislamiento es el mismo `WHERE` de siempre. Si después hace falta búsqueda
  semántica, `pgvector` está disponible para el PostgreSQL 16 del VPS (Anthropic no ofrece
  embeddings: sería otro proveedor o un modelo local).

### 4.6 Seguridad
- Aislamiento: el filtro por organización se aplica en el repositorio/consulta (como hoy
  `AlcanceDeOrganizaciones`) y se cubre con tests de «el usuario A no ve nada de B» en **cada**
  servicio y herramienta.
- Administradores asignados: nueva relación administrador ↔ cliente; `AlcanceDeOrganizaciones`
  pasa de «el equipo ve todo» a «el Owner ve todo, el admin lo asignado».
- Archivos: validar tipo **por contenido** (bytes mágicos, ya existe `DetectorDeImagen`), tamaño
  con tope al leer (ya existe `LectorConTope`), rechazar macros en Office.
- **[DECIDES TÚ] Antivirus.** **A.** ClamAV en el VPS (~1 GB de RAM: el VPS va justo).
  **B.** No ejecutar nada de lo subido y tratarlo como datos: parsear con bibliotecas, nunca abrir
  con Office, servir siempre como descarga. **C.** Escaneo externo (VirusTotal: no, porque
  publica los archivos). Recomiendo **B ahora y A cuando haya un servidor con memoria**, dicho en
  el aviso de privacidad.
- Retención y borrado: el SAT exige conservar la contabilidad 5 años. Borrado de cuenta = baja
  lógica + purga después del plazo legal, salvo lo que la ley obligue a conservar. Lo decide el
  despacho; la app lo hace configurable.

## 5. Plan por fases

Cada fase termina desplegada y usable.

| Fase | Qué entrega | Depende de |
|---|---|---|
| **0. Cimientos** (días) | Copia externa cifrada; llave de despliegue + CI; parche de dependencias vulnerables; administradores asignados; aviso de privacidad + consentimiento; consumo de IA por organización; vigilancia externa | Dudas 1, 3, 4 y decisión de almacenamiento |
| **1. Bóveda + CFDI** | Subir documentos (pantalla), almacenamiento cifrado, duplicados por hash/UUID, CFDI XML parseado y validado (FKE), pantalla de bóveda año→mes→tipo con filtros. Sin chat todavía | FKE (duda 2) |
| **2. Perfil fiscal + primer chat** | CSF parseada con código (modelo de respaldo), perfil con origen de cada dato, entrevista conversacional guiada por la taxonomía del FKE. Primer `IModeloConversacional`, primeras herramientas, cliente activo visible | Decisión de modelo del chat |
| **3. Estados de cuenta** | PDF (texto/escaneado), Excel/CSV con mapeo por banco, validación de saldos, revisión humana de lo que no cuadra | — |
| **4. Clasificación y conciliación** | Reglas (monto, fecha ±N, RFC/UUID) + FKE; propuestas con confirmación; faltantes («depósito sin factura»); el chat las explica y las resuelve | FKE |
| **5. El chat del equipo** | Cartera, trabajo sobre un cliente, borradores para pedir documentos, métricas del Owner | Fases 1-4 |
| **6. Lo que pidan los clientes** | Calendario de obligaciones, búsqueda semántica si hiciera falta, WhatsApp, descarga masiva del SAT | Dudas 8-10 |

**Lo que sugiero hacer primero, ya:** responder las dudas 1-4 y elegir almacenamiento; con eso
arranca la fase 0. La copia externa de la base no depende de nada y la puedo hacer en cuanto me
digas a dónde (Backblaze B2, Cloudflare R2, otro servidor o un disco tuyo).
