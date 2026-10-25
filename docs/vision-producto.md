# Visión del producto y reglas de trabajo

> Escrito por el dueño del proyecto el 2026-10-25. Es la fuente de verdad de QUÉ se construye y
> CÓMO se trabaja. Si algo del código o de CLAUDE.md lo contradice, gana esto y se corrige lo otro
> (o se pregunta). El plan vigente para llegar aquí está en [`plan/`](plan/).

## Tu rol
Eres el líder técnico y co-diseñador de producto de este proyecto. No eres un ejecutor pasivo: quiero que seas PROACTIVO. Eso significa:
- Si ves un riesgo, una mejor alternativa o una decisión que falta, dilo antes de que te lo pregunte.
- Propón features, validaciones y casos borde que yo no haya mencionado (sobre todo fiscales y de seguridad).
- Cuando una decisión sea reversible y de bajo impacto, tómala, documéntala y sigue. Cuando sea irreversible o cara (modelo de datos, proveedor de IA, almacenamiento, auth), detente y preséntame 2-3 opciones con pros/contras y tu recomendación.
- Al final de cada sesión dime: qué hiciste, qué decidiste por tu cuenta, qué queda pendiente y qué sugieres hacer después.

## Contexto
La app ya está desplegada en https://contable.ziacentro.mx pero sin features: solo el shell (título "Auxiliar contable", logo). Es parte del ecosistema ZIA.

## Visión del producto
Que el cliente lleve su contabilidad dentro de la plataforma usando IA como motor de contaduría. La experiencia debe sentirse como **platicar con tu contador**: el CHAT es la interfaz principal para operar la app; las pantallas tradicionales son de apoyo (consulta, búsqueda, revisión).

### Flujo principal del cliente
1. **Perfil fiscal (CSF opcional + entrevista):**
   - Al iniciar, el contador IA ofrece al cliente subir su **Constancia de Situación Fiscal (CSF)** en PDF para prellenar su perfil: RFC, nombre/razón social, CURP, régimen(es) fiscal(es), obligaciones, actividades económicas, domicilio fiscal, código postal y fecha de inicio de operaciones.
   - Si sube la CSF: extrae los datos, se los muestra para confirmar, y la entrevista continúa SOLO con lo que la CSF no cubre o lo que hay que aclarar (ej. si factura o solo recibe, bancos que usa, si tiene trabajadores, cómo maneja sus gastos). Valida que la CSF sea legible y reciente; si tiene más de ~3 meses o el régimen parece no coincidir con lo que el cliente cuenta, señálalo.
   - Si no quiere subirla: pasa directo a la entrevista conversacional completa, sin insistir.
   - La entrevista es natural y progresiva: persona física/moral, régimen (RESICO, plataformas digitales, actividad empresarial, arrendamiento, sueldos, etc.), giro, obligaciones (IVA, ISR, retenciones, DIOT, IMSS), periodicidad, etc. Las preguntas se adaptan a respuestas previas.
   - El perfil se guarda estructurado, el cliente lo puede revisar/corregir, y cada dato indica su origen: "extraído de CSF", "respondido por el cliente" o "inferido".
2. **Carga de documentos:** el cliente sube estados de cuenta (PDF, Excel, CSV), facturas (XML CFDI 4.0 y PDF), controles internos (Excel, Word, imágenes/fotos de tickets). Desde el chat ("te paso mis estados de cuenta de septiembre") o drag & drop.
3. **Organización asistida:** la IA identifica tipo de documento, periodo, emisor/receptor, montos, RFC, UUID; clasifica (ingreso/gasto/deducible/no deducible/por revisar), detecta duplicados y faltantes ("tienes un depósito de $12,500 sin factura que lo respalde") y conversa con el cliente para resolver ambigüedades.
4. **Bóveda documental:** acceso fácil a todo lo organizado. Estructura navegable (año → mes → tipo), filtros (RFC, monto, tipo, estado de clasificación), búsqueda de texto completo y búsqueda en lenguaje natural desde el chat ("enséñame las facturas de gasolina de agosto").

### El chat también es la herramienta de trabajo de Owners y Administradores
El mismo asistente sirve al equipo interno para atender a sus clientes, con capacidades distintas según el rol:
- **Vista de cartera:** "¿qué clientes tienen documentos sin clasificar?", "¿quién no ha subido estados de cuenta de octubre?", "¿qué clientes tienen obligaciones por vencer esta semana?".
- **Trabajo sobre un cliente específico:** el admin selecciona/menciona un cliente y el chat opera en su contexto: revisar y corregir clasificaciones, consultar su perfil, resumir su situación del periodo, detectar inconsistencias.
- **Preparación y comunicación:** generar resúmenes, listas de pendientes o borradores de mensajes para pedirle documentos faltantes al cliente.
- **Owner:** además, métricas de la operación (carga de trabajo por administrador, clientes activos, uso de la plataforma).
Reglas: el contexto del cliente activo debe ser explícito y visible en la UI (nunca ambiguo sobre "de quién estamos hablando"); un administrador solo accede a los clientes que tiene asignados; toda acción hecha por un admin sobre datos de un cliente queda en bitácora; y las respuestas del asistente hacia el equipo interno pueden ser más técnicas que hacia el cliente.

## Roles y permisos (multi-tenant)
- **Desarrollador:** todos los permisos, incluyendo configuración técnica y diagnóstico.
- **Owner:** dueño de la app; administra la plataforma, organizaciones, administradores y configuración de negocio.
- **Administrador:** empleado del owner; gestiona organizaciones/clientes asignados, puede revisar y corregir clasificaciones.
- **Usuario:** cliente, pertenece a una organización; solo ve y opera los datos de su organización.
Aislamiento estricto por organización en TODAS las capas (queries, storage, embeddings/índices de búsqueda, contexto del LLM). Las tools que el LLM puede invocar se filtran por rol y por alcance de datos del usuario que conversa. Diseña permisos como RBAC con permisos granulares, no ifs por rol regados en el código.

## Conocimiento fiscal
Tengo un Fiscal Knowledge Engine (FKE) en C#: catálogo de 250+ reglas CFDI para 19 regímenes, motores TAX, CFDI, EVID, RISK, SCORE, VALID, CONFLICT, y una taxonomía de 87 estructuras de negocio mexicanas. Pregúntame dónde está antes de reimplementar lógica fiscal; la idea es que el contador IA use esas reglas como herramientas (tools), no que el LLM "invente" criterio fiscal. La taxonomía de negocios es buena candidata para guiar la entrevista del perfil.

## Principios de IA (no negociables)
- El LLM orquesta y conversa; los cálculos, validaciones de CFDI y reglas fiscales se hacen en código determinista expuesto como tools/function calling.
- Nunca inventar cifras. Toda cifra mostrada debe rastrearse a un documento fuente (trazabilidad documento → dato → clasificación).
- Distinguir siempre "dato extraído", "dato inferido" y "dato confirmado".
- Human-in-the-loop: la IA propone clasificaciones, el cliente/administrador confirma. Nada se da por definitivo sin confirmación.
- Disclaimer claro: es un asistente, no sustituye la firma de un contador para declaraciones.
- Diseña la capa de LLM desacoplada del proveedor (interfaz propia) para poder cambiar de modelo o usar modelos on-premise después.
- Controla costo: extracción determinista primero (XML CFDI se parsea, la CSF tiene formato fijo y se intenta parsear antes de usar LLM), OCR/LLM solo cuando haga falta, cachea resultados.

## Seguridad y cumplimiento
Datos financieros y fiscales sensibles: cifrado en reposo y tránsito, URLs firmadas para documentos, bitácora de auditoría (quién vio/cambió qué), retención y borrado de datos, consideraciones de LFPDPPP. Valida tipo y tamaño de archivo, escanea uploads. No mandes al LLM más datos de los necesarios.

## Aclaraciones posteriores del dueño

- **2026-10-25** — Hay **un solo despacho** (no es multi-tenant en el sentido de varios
  despachos). **El Owner y los administradores son del mismo despacho y ven todos los
  clientes**; un cliente pertenece a un solo despacho. Esto sustituye a «un administrador solo
  accede a los clientes que tiene asignados» y a «Roles y permisos (multi-tenant)» de arriba: el
  aislamiento estricto es **entre clientes (organizaciones)**, no entre administradores.
- **2026-10-25** — Un cliente = un RFC. La ubicación del FKE aún no se conoce.
- **2026-10-25** — Los documentos se guardan en el disco del VPS (cifrados por la aplicación) y
  el antivirus va en el VPS.
