/**
 * El manual de la consola. Es contenido, no lógica: está aquí y no en la base para que viaje
 * con el código que describe (si cambia una pantalla, cambia en el mismo commit).
 *
 * ⚠️ Cada tema dice QUÉ PERMISO hace falta para lo que explica, y solo se enseña a quien lo
 * tiene. No va por rol: el Owner puede cambiar los permisos de cada rol, y un manual por rol
 * explicaría pantallas que esa persona ya no ve (o callaría las que sí).
 */

export interface Tema {
  titulo: string;
  icono: string;
  /** Párrafos cortos. */
  texto?: string[];
  /** Pasos numerados. */
  pasos?: string[];
  /** Un consejo o una advertencia al final. */
  nota?: string;
  /** Pantalla a la que lleva, si la hay. */
  ruta?: string;
  /** Basta con UNO de estos permisos. Sin permisos: para todo el mundo. */
  permisos?: string[];
  /**
   * Se esconde a quien tenga ALGUNO de estos. Para lo que solo aplica al cliente: al equipo
   * (que ve a todos los clientes) le explicaría algo que no le pasa.
   */
  salvoPermisos?: string[];
  /** Nivel mínimo en la jerarquía (3 = Developer). Solo para lo que no depende de un permiso. */
  nivelMinimo?: number;
  /** Solo si el chat con IA está encendido en el servidor (sin API de Claude se esconde). */
  chat?: boolean;
}

export interface Seccion {
  titulo: string;
  temas: Tema[];
}

export const RESUMEN_POR_NIVEL: Record<number, { rol: string; texto: string }> = {
  0: { rol: 'Usuario', texto: 'Ves y subes la información de tu organización (según tu papel en ella) y gestionas tu propia cuenta.' },
  1: { rol: 'Administrador', texto: 'Ves la información de todos los clientes, los organizas y das de alta a los Usuarios.' },
  2: { rol: 'Owner', texto: 'Eres el dueño del negocio: gestionas a tu gente, sus roles y la imagen de la consola.' },
  3: { rol: 'Developer', texto: 'Mantienes la consola: tienes todos los permisos y nadie más ve tu cuenta.' },
};

export const SECCIONES: Seccion[] = [
  {
    titulo: 'Lo básico',
    temas: [
      {
        titulo: 'Entrar por primera vez',
        icono: 'bi-box-arrow-in-right',
        pasos: [
          'Abre el enlace del correo «Tu invitación a Auxiliar contable». Vale 7 días y sirve una sola vez.',
          'Elige tu usuario y tu contraseña. Al hacerlo tu correo queda confirmado.',
          'Entra con tu usuario (o tu correo) y la contraseña que elegiste.',
        ],
        nota: '¿Te dieron la cuenta sin invitación? Entra con el usuario y la contraseña que te pasaron y cámbiala en «Seguridad».',
      },
      {
        titulo: 'Protege tu cuenta',
        icono: 'bi-shield-lock',
        ruta: '/seguridad',
        pasos: [
          'Ve a Mi cuenta → Seguridad.',
          'Registra tu correo y escribe el código que te llega para verificarlo.',
          'Registra tu celular con código de país (por ejemplo +52 55 1234 5678).',
          'Activa la verificación en dos pasos: al entrar, además de la contraseña, te pediremos un código.',
          'Guarda los 10 códigos de recuperación que aparecen al activarla: cada uno te deja entrar una vez si no te llega el código.',
        ],
        nota: 'Nadie del despacho te va a pedir un código por teléfono ni mensaje. Si alguien lo hace, no se lo des.',
      },
      {
        titulo: 'Si olvidas la contraseña',
        icono: 'bi-key',
        pasos: [
          'En la pantalla de entrar, pulsa «¿Olvidaste tu contraseña?» y escribe tu correo.',
          'Te llega un correo con un botón para elegir una nueva. El enlace caduca pronto y sirve una vez.',
        ],
        nota: 'Si no tienes correo verificado, pide a quien te dio de alta que te ponga una contraseña nueva.',
      },
      {
        titulo: 'Tu cuenta',
        icono: 'bi-person-circle',
        ruta: '/mi-cuenta',
        texto: [
          'Desde el menú de tu nombre llegas a Mi cuenta: tus datos, tu seguridad, tu foto, tus sesiones y tus avisos.',
          'En Sesiones ves desde qué equipos has entrado y puedes cerrar las que no reconozcas.',
        ],
      },
      {
        titulo: 'Trucos',
        icono: 'bi-lightbulb',
        texto: [
          'Ctrl + K (⌘ + K en Mac) abre el buscador para saltar a cualquier pantalla.',
          'El botón del sol / la luna, arriba a la derecha, cambia entre modo claro y oscuro.',
        ],
      },
    ],
  },
  {
    titulo: 'Asistente',
    temas: [
      {
        titulo: 'Platica con el asistente',
        icono: 'bi-chat-dots',
        ruta: '/asistente',
        chat: true,
        salvoPermisos: ['AuxiliarContable.Organizaciones'],
        pasos: [
          'Al entrar te recibe el asistente: escríbele como a tu contador, con tus palabras y de lo que quieras: «tengo una panadería», «¿qué me falta?», «¿me conviene…?».',
          'Antes de hacer algo, pregúntale: «voy a comprar una camioneta», «me quieren pagar en efectivo», «me salió un cliente en Estados Unidos». Revisa la ley con tu perfil y te dice qué cuidar: si lo puedes deducir, a nombre de quién va la factura, cómo pagar y qué guardar.',
          'Con el clip (o arrastrando) le mandas facturas XML, estados de cuenta, tickets o tu constancia: los guarda en tus documentos y te dice qué llegó.',
          'Pídele tus datos en Excel, PDF, Word, PowerPoint o CSV («pásame mis gastos de octubre en Excel», «hazme una presentación con mis impuestos del año»): lo arma y abajo de su respuesta sale el botón para bajarlo. Cualquier respuesta con tabla trae además sus botones de Excel y PDF.',
          'En el celular, la tecla Enter baja de renglón y mandas con la flecha; en la computadora, Enter manda y Shift+Enter baja de renglón. Si lo que escribes es largo, desliza dentro de la caja para ver el principio. Si sales del navegador mientras te contesta, la respuesta se termina igual y la ves al volver.',
          'Arriba ves cómo vas (perfil, documentos del mes, constancia): toca una cifra para preguntarle por ella.',
          'Lo que le digas de tu negocio lo anota en tu perfil fiscal; abajo de su respuesta ves qué guardó.',
          'Si algo quedó mal, díselo o corrígelo en Perfil fiscal.',
          'Pregúntale qué dice la ley («¿puedo deducir mi coche?»): busca en las leyes fiscales vigentes y te contesta con los artículos al final, en «Fundamento».',
          'Pregúntale «¿qué clave uso para…?» o «¿está bien la clave de esta factura que me llegó?»: busca la clave por lo que es el producto, revisa la clave, la unidad y la descripción de cada concepto y te avisa si algo no cuadra (en una recibida, te dice qué pedirle al proveedor). En «Toda la cartera» también: si no dices de qué cliente, busca la clave sin revisar la constancia de nadie.',
          'Si le pides un cálculo, te lo muestra en una tabla paso por paso (concepto, operación e importe); en el teléfono la tabla se desliza de lado.',
          'Si le cuentas una situación (un proveedor, una carta del SAT, una devolución), también revisa cómo la han resuelto los tribunales y te dice qué conviene guardar.',
          'Pregúntale por tus cobros y pagos: «¿ya pagué la factura de Telmex?», «¿qué gastos de octubre no tienen factura?». Busca en tu información y te da fechas, importes y si falta la factura o el pago.',
          'Pregúntale con tus palabras «¿cuánto gasté de gasolina este mes?» o «compré un refrigerador, ¿es deducible?»: con tu giro y tu régimen te dice qué es eso para ti (mercancía que revendes, inversión, gasto o gasto personal), te suma lo que llevas y cuánto no tiene factura, y solo te pregunta lo que de verdad le falta.',
          'Pregúntale por qué un gasto no se dedujo: te explica el motivo con su artículo y, si se puede, cómo corregirlo.',
          'También te dice qué leyes estatales y reglamentos de tu municipio te aplican por tu giro. Puede consultar, pero no cambia tu contabilidad: eso lo hace el despacho.',
          'Si algo quedó mal, pídeselo: te propone el texto y, si le dices que sí, le deja la solicitud al despacho. Ahí mismo ves la respuesta y lo que el despacho te ha pedido; cuando lo subas, díselo y lo marca como atendido.',
        ],
        nota: 'Es orientación general hecha con IA y puede equivocarse: las decisiones fiscales las revisa tu contador.',
      },
      {
        titulo: 'La cartera del despacho',
        icono: 'bi-grid-3x3-gap',
        ruta: '/cartera',
        permisos: ['AuxiliarContable.Organizaciones'],
        texto: [
          'Cartera muestra a todos los clientes del mes con un semáforo: urgente (sin RFC o sin estado de cuenta del mes), atención o al día.',
          'Cada tarjeta dice qué falta y lleva directo a su conciliación, documentos o perfil.',
          'El Owner ve además las métricas del despacho. Las flechas cambian de mes.',
        ],
      },
      {
        titulo: 'El asistente para el despacho',
        icono: 'bi-chat-dots',
        ruta: '/asistente',
        chat: true,
        permisos: ['AuxiliarContable.Organizaciones'],
        texto: [
          'Al entrar abre «Toda la cartera» con el semáforo del mes. Ahí el equipo (auxiliar, administrador, despacho) le pide lo que sea de cualquier cliente diciendo cuál («¿cómo va la conciliación de octubre de Panadería Lupita?», «guárdale su régimen a Taller Pepe»): no hace falta elegirlo antes. Para subir documentos o platicar solo de uno, toca la pastilla de arriba y cambia a ese cliente: queda siempre a la vista con quién estás platicando.',
          'Pídele tus datos en Excel, PDF, Word, PowerPoint o CSV («pásame mis gastos de octubre en Excel», «hazme una presentación con mis impuestos del año»): lo arma y abajo de su respuesta sale el botón para bajarlo. Cualquier respuesta con tabla trae además sus botones de Excel y PDF.',
          'Pídele un resumen del perfil, qué falta o un borrador de mensaje para el cliente (no lo envía: lo copias tú).',
          'Pídele «¿qué falta conciliar de octubre?»: cruza sus estados de cuenta con sus facturas y te dice qué depósitos y pagos no tienen factura.',
          'Pregúntale por la ley (deducciones, plazos, tasas): contesta citando ley y artículo.',
          'Cuéntale una operación que el cliente piensa hacer: arma lo que la ley pide revisar para su perfil (leyes que se activan, preguntas obligatorias, artículos) y te orienta.',
          'En el chat de un cliente busca sus operaciones («¿ya se pagó la factura X?») y explica por qué un gasto no se dedujo, con el motivo y el artículo que dejó el cálculo.',
          'También lee las pólizas y el pago provisional guardados del mes («¿cómo va el ISR de octubre?», «la póliza de Telmex»). Todavía no corrige desde el chat: te dice qué y dónde corregir.',
          'Solicitudes: en el chat de un cliente pídele que le solicite un documento o un dato (le queda pendiente al cliente) o atiende lo que el cliente pidió; en «Toda la cartera» pregúntale «¿qué nos han pedido?» y te da lo pendiente de todos, lo más viejo primero.',
          'Lo que guarde a partir de lo que le digas queda como «Del despacho». Cada quien tiene su propia conversación.',
        ],
      },
    ],
  },
  {
    titulo: 'Documentos',
    temas: [
      {
        titulo: 'Mi información: facturas, dinero y control interno',
        icono: 'bi-folder2-open',
        ruta: '/mi-informacion/facturas',
        salvoPermisos: ['AuxiliarContable.Organizaciones'],
        pasos: [
          'Abre «Mi información». Tiene cuatro pestañas: Facturas, Dinero, Control interno y Todo.',
          '«Facturas»: sube el XML de tus facturas (y su PDF si lo tienes), o cambia a «Pedir factura» para que el despacho te la timbre.',
          '«Dinero»: estados de cuenta y comprobantes de transferencias; o cambia a «Efectivo y crédito» para lo que no pasa por el banco.',
          '«Control interno»: tickets, notas de venta o compra y comprobantes internos.',
          'Al subir algo te preguntamos por las otras dos partes de esa operación: el UUID de su factura, la referencia o folio de la transferencia (o si fue en efectivo o a crédito) y su ticket o nota. Si no lo tienes, dilo («no se facturó», «no tengo la referencia»); si no lo sabes ahorita, déjalo para después.',
          '«Todo»: cada operación del mes con tres círculos (factura · transacción · control interno). Verde es amarrada, ámbar es «no hay» y gris es pendiente. Las más completas van primero; toca una para completarla o juntarla con otra.',
          'En «Facturas», elige un mes y pulsa «Ver clasificadas»: tus facturas emitidas y recibidas por tipo, las PPD por definir, las inversiones y qué es cada compra para tu giro.',
          '¿Quedó en otro mes? Toca el calendario junto al documento y cámbialo.',
          'Navega por año y mes, o busca por el nombre del archivo.',
        ],
        nota: 'Si subes el mismo archivo dos veces no se duplica. Todo se guarda cifrado.',
      },
      {
        titulo: 'Mesa de trabajo',
        icono: 'bi-stars',
        ruta: '/mesa',
        permisos: ['AuxiliarContable.Organizaciones'],
        pasos: [
          'Elige al cliente y el mes: ves todas sus operaciones, completas o no, con sus tres círculos (factura · transacción · control interno).',
          '«Impuestos al día»: el ISR y el IVA a pagar del mes, según el régimen del perfil fiscal. Se recalculan solos 10 minutos después de la última factura que entre (lo que sube el cliente, lo que baja el Descargador 1 o lo que factura el despacho), cada noche a las 12 y cuando pulsas «Actualizar ahora». «Declaración anual»: «Calcular la anual» saca el ISR del ejercicio con las facturas del año (persona física art. 152 con deducciones personales, RESICO art. 113-F o persona moral art. 9 con el coeficiente de utilidad para el año siguiente), menos los pagos provisionales y las retenciones; captura antes lo que no viene en las facturas (PTU, pérdidas, UMA, inventarios). Los sueldos salen solos de los recibos de nómina timbrados donde el cliente es el trabajador, y lo retenido por plataformas, bancos (intereses) y sociedades (dividendos) de sus CFDI de retenciones: súbelos como facturas en la bóveda; se acomodan en el último mes de su periodo. «Nómina» (menú): da de alta a los trabajadores de cada cliente (salario diario, ingreso, crédito INFONAVIT o FONACOT) y crea cada periodo (semanal, catorcenal, quincenal o mensual); captura las incidencias (faltas, incapacidades, horas extra, domingos, descansos y festivos trabajados, aguinaldo, prima vacacional) y «Calcular la nómina» da por trabajador lo gravado y lo exento, el ISR con el subsidio 2026, el SDI y el SBC, las cuotas del IMSS por ramo (obrero y patrón), INFONAVIT, el neto, el impuesto sobre nóminas del estado y la póliza. «Avisos al IMSS»: las altas, modificaciones de salario y bajas se registran solas; «Descargar archivos del IDSE» los arma con el formato del IMSS (168 posiciones) para subirlos, y «Ya los presenté» los quita de lo pendiente. «Recibos para timbrar» da un .zip con el CFDI 4.0 de nómina de cada trabajador, sin sello, validado contra el XSD del SAT, para que tu PAC lo selle y timbre. «Cuotas del mes» suma las del IMSS e INFONAVIT de los periodos para cotejarlas con el SUA o el SIPARE. En el perfil fiscal del cliente pon su registro patronal, su prima de riesgo y la guía del IMSS; a cada trabajador, sus apellidos, CURP, NSS y código postal. «Interpretar con Claude» (en Operaciones): Claude lee juntos la factura (cada concepto con su clave SAT y su unidad, y quién es el proveedor), el ticket o nota y el estado de cuenta; primero dice qué es de verdad el objeto y si las fuentes coinciden, y luego qué es para el régimen y giro del cliente: deducible o no, inversión y su %, ingreso, IVA, la cuenta, el juicio contable, lo que pudo ser otra cosa, los requisitos fiscales que faltan y qué preguntarle al cliente. Lo hace en segundo plano (unas dos por minuto) y solo vuelve a preguntar si cambia alguna fuente; las pólizas usan su categoría cuando está segura y sus alertas entran a la revisión. Además, la revisión de las pólizas avisa de lo no deducible y de los requisitos que no se cumplen (restaurantes, obsequios, autos y su tope, efectivo, uso o régimen del receptor…) con el artículo según el régimen. «DIOT»: «Armar la DIOT» la saca de las facturas recibidas por flujo de pagos (las PPD en el mes de cada complemento): un renglón por proveedor con de dónde sale cada peso, lo que no entra y por qué, y el archivo de carga masiva para el SAT; vence el último día del mes siguiente y el RESICO está relevado. «Ver el cálculo renglón por renglón» enseña el ISR y el IVA del mes como en el papel de trabajo, con el artículo de cada renglón. A las personas morales ponles el coeficiente de utilidad en su perfil. A quien está en plataformas tecnológicas (625) contéstale en su perfil si paga con pago definitivo o con pagos provisionales y qué hace por la plataforma (la tasa); sin eso no se calcula su ISR. «Papel de trabajo (Excel)» lo arma con fórmulas y fundamento.',
          '«Descarga del SAT» → «Descarga Masiva con la e.firma del cliente»: con la e.firma guardada en Credenciales, «Pedir … al SAT» usa el servicio oficial (sin captcha ni robot); el SAT arma los paquetes en minutos u horas y la plataforma los baja sola, sin duplicar por UUID. Desde el día 3 de cada mes se pide sola la del mes anterior para el cliente que no mandó nada. Cada uso de la e.firma queda en su bitácora.',
          'De respaldo, el Descargador 1: si el cliente no entregó nada, marca «El cliente no entregó nada». Baja la orden (meses, emitidas y/o recibidas), ábrela en el Descargador 1 con «Orden de la plataforma…» e inicia sesión en el SAT con el RFC del cliente: lee el estatus de todo y baja solo lo que falta o está dañado. Sube aquí el paquete .zip que deja. El estatus de las facturas (vigente, cancelada, en proceso de cancelación) se revisa solo cada noche con el servicio público del SAT; «Revisar estatus en el SAT» lo hace en ese momento.',
          '«Analizar con Claude»: lee los tickets, notas, comprobantes y fotos que subió el cliente; saca fecha, importe, folio, referencia y UUID, dice qué es para su giro y completa la operación.',
          '«Amarrar el banco» (después de conciliar en «Cierre del mes»): cada movimiento se vuelve operación y se amarra por UUID, luego por importe y fecha, y lo que quede con Claude.',
          'Lo que Claude propone con duda aparece en «Claude sugiere»: acéptalo con «Sí, júntalas» o descártalo.',
          'Filtra por lo que falta (sin factura, sin transacción, sin control interno) y abre una operación para completarla.',
          '«Revisar y generar pólizas»: primero revisa que la información no traiga errores (pago distinto de la factura, PUE sin pago, efectivo de más de $2,000, ingreso sin factura, ticket de otro RFC, posible duplicado, gasto ajeno al giro). Lo que «detiene» no se contabiliza hasta que lo corrijas o pulses «Está bien, contabilízala».',
          'Pagos y costo de ventas: una factura de un mes anterior que se paga en este lleva su póliza de pago (y el IVA pasa a pagado). Captura el inventario final (analítico) o el costo de lo vendido (perpetuo) y vuelve a generar: sale la póliza de costo de ventas; el inventario final pasa como inicial al mes siguiente.',
          'Contabilidad al día: 10 minutos después de lo último que entra del mes (facturas, estados de cuenta, tickets, efectivo y crédito, cambios a las operaciones), la plataforma concilia el banco si cambiaron sus archivos, amarra lo seguro (por UUID y por regla; lo dudoso con Claude sigue siendo con «Amarrar el banco») y rehace las pólizas con el inventario que capturaste. Con eso el control interno del mes también queda al día. Arriba de las pólizas dice cuándo se puso al día y qué hizo; «Actualizar ahora» no espera. Un mes cerrado no se toca.',
          '«Balanza, estados financieros y contabilidad electrónica»: captura una vez los saldos de apertura (pega de Excel cuenta, deudor y acreedor); luego «Generar balanza, estados y XML» da la balanza acumulada del mes, el estado de resultados del ejercicio y el de situación financiera al cierre del mes (NIF B-3 y B-6, con sus razones financieras; si faltan saldos iniciales o el costo de lo vendido salen como preliminares); da clic en una cuenta de la balanza para ver su auxiliar del mes: saldo inicial, cada cargo y abono con su póliza y el saldo corrido y descarga el catálogo, la balanza y las pólizas en XML del SAT, validados contra su esquema, con la fecha en que vencen.',
          'Las cuentas dependen del tipo de empresa (comercial, servicios, fabricante, constructora) y del sistema de costos e inventarios: ponlos en el perfil fiscal del cliente. Abajo están las pólizas, la balanza del mes y lo pendiente para el cierre (costo de ventas, producción, depreciación).',
        ],
        nota: 'Lo que Claude ya leyó no se vuelve a mandar. Nunca escribe encima de lo que dijo el cliente: solo llena lo que falta.',
      },
      {
        titulo: 'La bóveda de cada cliente',
        icono: 'bi-folder2-open',
        ruta: '/mi-informacion/facturas',
        permisos: ['AuxiliarContable.Organizaciones'],
        texto: [
          'Elige al cliente: ves sus documentos por mes y por tipo, y sube cada cosa en su zona (facturas, estados de cuenta y dinero, control interno).',
          'Con un mes elegido, «Ver clasificadas» enseña sus facturas por categoría según su giro (el del perfil fiscal): mercancía, insumo, gasto, inversión o ajeno al giro.',
          'Un mes sin facturas o sin estado de cuenta es lo que hay que pedirle.',
        ],
      },
      {
        titulo: 'Cerrar el mes de un cliente',
        icono: 'bi-arrow-left-right',
        ruta: '/conciliacion',
        permisos: ['AuxiliarContable.Organizaciones'],
        pasos: [
          'En Cartera, cada cliente dice en qué va su mes: faltan documentos, por conciliar, con pendientes, listo para cerrar o cerrado. Tócalo para abrir su cierre.',
          'Faltan documentos: que estén el estado de cuenta (se acomoda solo en su mes) y las facturas XML, emitidas y recibidas.',
          'Pulsa «Conciliar»: cada cuenta dice si cuadra con el banco y cada movimiento qué factura le corresponde. La conciliación se guarda.',
          'En «Movimientos sin factura» di qué es cada uno (traspaso entre sus cuentas, real sin factura, factura por llegar, no lleva factura) o pregúntale al cliente.',
          'En «Facturas que no cuadran», «Revisar facturas» junta cada factura con sus complementos de pago y notas de crédito y dice qué está mal (p. ej. un complemento que paga la factura PUE en vez de la PPD) para que se corrija con quien la timbró.',
          'Cuando todo está resuelto, «Cerrar el mes». Cerrado, nadie puede subir, mover ni borrar documentos de ese mes; si hace falta, «Reabrir el mes».',
        ],
        nota: 'Si llega un documento después de conciliar, el cierre pide volver a conciliar; lo que ya decidiste de cada movimiento se conserva. El cliente necesita su RFC en el perfil fiscal.',
      },
      {
        titulo: 'Contesta lo que te pregunta el despacho',
        icono: 'bi-chat-left-text',
        ruta: '/conciliacion',
        salvoPermisos: ['AuxiliarContable.Organizaciones'],
        pasos: [
          'Abre «Cierre del mes» y elige el mes: ves en qué va y qué falta.',
          'En «Movimientos sin factura», si el despacho te preguntó por alguno, escribe qué es y toca «Responder».',
          'Si te falta subir algo (el estado de cuenta o facturas), ahí mismo está el botón para ir a Documentos.',
        ],
      },
    ],
  },
  {
    titulo: 'Perfil fiscal',
    temas: [
      {
        titulo: 'Llena tu perfil con tu constancia',
        icono: 'bi-person-lines-fill',
        ruta: '/perfil-fiscal',
        salvoPermisos: ['AuxiliarContable.Organizaciones'],
        pasos: [
          'Descarga tu Constancia de Situación Fiscal (PDF) del portal del SAT. Que sea reciente: de los últimos 3 meses.',
          'Abre Perfil fiscal y toca «Subir mi constancia»: se guarda en tu expediente y se leen de ella tu RFC, nombre, código postal, regímenes, obligaciones y actividades.',
          'Revisa lo que dice «De tu constancia» y confírmalo (o toca «Confirmar todo»). Si algo está mal, corrígelo.',
          'Completa lo que la constancia no dice (a qué te dedicas, si facturas, tus bancos, si tienes trabajadores).',
        ],
        nota: 'Nada se da por bueno hasta que lo confirmas. Cada dato dice de dónde salió: de tu constancia, de ti o del despacho.',
      },
      {
        titulo: 'El perfil fiscal de cada cliente',
        icono: 'bi-person-lines-fill',
        ruta: '/perfil-fiscal',
        permisos: ['AuxiliarContable.Organizaciones'],
        texto: [
          'En Perfil fiscal elige al cliente: ves su constancia, sus datos y de dónde salió cada uno.',
          'Puedes subir su constancia por él, confirmar datos y corregirlos; lo que captures queda como «Del despacho».',
        ],
      },
    ],
  },
  {
    titulo: 'Leyes',
    temas: [
      {
        titulo: 'Consultar leyes',
        icono: 'bi-journal-text',
        ruta: '/leyes',
        pasos: [
          'Abre «Consultar leyes» en el menú.',
          'Escribe la cita («artículo 27 LISR») o lo que buscas con tus palabras («deducción de automóviles»).',
          'Si sabes de qué ley es, elígela para acotar (LISR, IVA, CFF, Miscelánea, NIF…).',
          'Cada resultado trae la ley, el artículo y su última reforma; el botón del portapapeles copia la cita.',
        ],
        nota: 'Es el texto de la ley tal cual, sin interpretación: lo que hay que hacer en tu caso lo decide tu contador.',
      },
      {
        titulo: 'Mis leyes estatales y municipales',
        icono: 'bi-journal-text',
        ruta: '/concentrado',
        texto: [
          'Si el sistema no calcula de acuerdo a tu ley estatal o municipal, sube las leyes en este apartado y se trabajará a partir de ahí.',
        ],
        pasos: [
          'En la hoja concentrado abre la pestaña «Mis leyes».',
          'Arriba ves «Leyes y reglamentos que te aplican»: por tu domicilio fiscal y lo que haces (giro, trabajadores, local, inmuebles, vehículos, ventas al gobierno) te decimos qué leyes estatales y qué reglamentos de tu municipio te tocan (licencia de funcionamiento, protección civil, anuncios, uso de suelo, construcción, residuos…), por qué y qué te piden. Las que ya tenemos las agregas con «Usar esta ley», sin subirlas.',
          'Si tu municipio aún no está en la plataforma te lo avisamos: de él solo tenemos lo federal y lo estatal. En cada ley o reglamento que nos falte, «Súbela tú» llena el formulario por ti.',
          'Si tu municipio no tiene un reglamento propio de un tema (por ejemplo, de licencias de funcionamiento), te decimos qué otros reglamentos de tu municipio lo tratan y en qué artículos (el Bando, el de mercados, el de comercio en vía pública…).',
          'Si te falta alguna, o es municipal, sube el PDF de la ley (ley de ingresos, de hacienda, código fiscal, reglamento de protección civil, de comercio, de anuncios…), di si es estatal o municipal, el estado y, si es municipal, el municipio.',
          'Se lee artículo por artículo (las páginas escaneadas también) y se busca lo que te toca según tu giro y tu perfil: tasa y día del impuesto sobre nómina, recargos, licencias, protección civil, estímulos…',
          'Cada hallazgo trae la cita textual del artículo. El despacho lo revisa: lo que acepta cambia tus cálculos solo a ti; las obligaciones locales aparecen en «Deudas fiscales».',
          'Abajo puedes preguntar sobre tus leyes; la respuesta sale solo de lo que subiste, con su artículo.',
        ],
        nota: 'Es para llevar el control de todas tus obligaciones de acuerdo con tu negocio; no te apoyamos con trámites presenciales (por ahora, solo en Morelos). Si cambias de giro, contratas trabajadores o abres un local, tus leyes se vuelven a revisar solas. Para que la lista sea exacta, contesta en tu perfil si tienes local, inmuebles o vehículos y si vendes al gobierno.',
      },
      {
        titulo: 'Cobertura municipal (despacho)',
        icono: 'bi-map',
        ruta: '/cobertura-municipal',
        texto: [
          'Mapa de control: qué leyes estatales y qué reglamentos municipales tenemos de cada estado y municipio, medidos contra todo el catálogo (72 leyes estatales y 40 reglamentos municipales).',
          'Cada norma cae en una clase: tenemos; no aplica (A: lo regula el estado, la federación o el municipio; B: no existe ahí o la cubre otra norma); no la encontramos en línea (C: debería existir y no está en ninguna fuente oficial de internet; puede existir sin publicarse); o sin revisar.',
        ],
        pasos: [
          'Abre «Cobertura municipal» en el menú (solo el equipo del despacho).',
          'Arriba, las cifras: estados con todas sus leyes, municipios elegidos con sus reglamentos principales, municipios completos y lo que falta revisar.',
          'En el mapa de México elige qué colorear: «Municipal» (cuánta población vive en un municipio cubierto) o «Estatal» (cuánto del catálogo estatal tenemos). Toca un estado para ver su expediente.',
          'En el expediente: la barra de sus leyes estatales y «Ver ley por ley»; sus municipios con ficha, cada uno con su barra (toca uno para ver reglamento por reglamento, con el motivo de lo que no aplica, qué otros reglamentos lo tratan y la fuente oficial); y los municipios no elegidos, agrupados por el motivo.',
          'Abajo, los clientes en un municipio no cubierto o del que no se supo el municipio: de ellos solo tenemos lo federal y lo estatal.',
          '«Descargar CSV» baja la cobertura para Excel; «Municipios de clientes para el vigía» baja prioridad.txt (solo estado/municipio, sin nombres) para la revisión mensual de reglamentos.',
        ],
        nota: 'Un municipio que ya tiene todo lo que le aplica cuenta como cubierto aunque tenga pocos reglamentos. El cliente de un municipio no cubierto lo ve en su perfil fiscal y en «Mis leyes», y puede subir sus reglamentos.',
      },
      {
        titulo: 'Efectivo y crédito',
        icono: 'bi-cash-coin',
        ruta: '/mi-informacion/dinero/efectivo',
        pasos: [
          'Abre «Efectivo y crédito» y elige el mes: arriba ves cuánto entró y salió en efectivo, lo que falta por pagar y por cobrar.',
          'Agrega una entrada en efectivo (una venta o el corte de caja del día), una salida en efectivo (un gasto), una compra a crédito o una venta a crédito.',
          'Si tienes el ticket, la foto o la factura, súbelo ahí mismo: queda en Documentos y ligado al registro.',
          'Lo comprado o vendido a crédito aparece cada mes hasta que pulses «Ya se pagó» o «Ya se cobró».',
        ],
        nota: 'Lo que no pasa por el banco también es ingreso, gasto o deuda: sin esto el mes queda incompleto. Un mes cerrado ya no se puede cambiar.',
      },
      {
        titulo: 'Pedir factura',
        icono: 'bi-receipt',
        ruta: '/mi-informacion/facturas/pedir',
        pasos: [
          'Abre «Pedir factura» y pulsa «Pedir una factura».',
          'A quién: elige uno de antes o sube su Constancia de Situación Fiscal para llenar RFC, nombre, código postal y régimen exactos.',
          'Uso del CFDI y método de pago (PUE si ya te pagaron, PPD si te pagan después: lleva complemento de pago).',
          'Los conceptos con su clave del SAT, cantidad, unidad, precio e IVA. Recomendación: precio más IVA; si tu precio ya trae el IVA, marca la casilla.',
          '¿No sabes la clave? Toca «Buscar clave» y escribe cómo llamas al producto («reflector LED 50 W»): te propone claves con su unidad; toca una para usarla. Si tus actividades de la constancia no cubren esa venta, te avisa cuál sí.',
          'Al enviar se revisa que cada clave cuadre con su descripción y su unidad (p. ej. un servicio con unidad «Pieza»). Si hay avisos los ves en el concepto: corrígelos o, si estás seguro, «Enviar así».',
          'El despacho la timbra y en la solicitud aparecen el XML y el PDF (también en Documentos); si no se puede, te dice por qué.',
        ],
        nota: 'Por ahora el despacho timbra en su sistema; la emisión automática se conecta cuando se acuerde el plan de facturación.',
      },
      {
        titulo: 'Control interno',
        icono: 'bi-clipboard-check',
        ruta: '/mi-informacion/control/expedientes',
        pasos: [
          'Abre «Control interno» y elige el mes: salen las salidas de dinero del banco (de la conciliación del mes) y las de «Efectivo y crédito».',
          '«Falta probar que ocurrió»: el gasto tiene factura, pero hace falta el contrato, el entregable, una foto o el pedido.',
          '«Falta comprobante o explicación»: no tiene factura; sube el comprobante o escribe qué fue.',
          'Con «Agregar evidencia» sube el archivo (queda en Documentos) o escribe la explicación, o las dos cosas.',
        ],
        nota: 'Ante una revisión del SAT, la factura y el registro contable solos no bastan para probar que la operación existió (materialidad).',
      },
      {
        titulo: 'Mis credenciales',
        icono: 'bi-shield-lock',
        ruta: '/credenciales',
        pasos: [
          'Abre «Mis credenciales»: ves la e.firma (RFC, número de certificado y hasta cuándo es vigente) y la contraseña del portal (CIEC).',
          'La primera vez pon tu NIP de 4 dígitos (te pide la contraseña de tu cuenta). Es tuyo y abre las credenciales de los clientes que puedes ver.',
          '«Ver contraseña» o «Bajar .key» te piden el NIP (o la contraseña de tu cuenta); la contraseña se oculta sola al minuto.',
          'Para agregarlas o cambiarlas: el .cer, el .key y su contraseña. Se comprueba que la contraseña abra la llave y que la llave sea de ese certificado.',
          'En «Quién las ha usado» queda cada vez que alguien vio, bajó o cambió algo.',
        ],
        nota: 'Ver contraseñas y bajar archivos lo hacen el administrador de la organización y el despacho. Cinco intentos fallidos bloquean 15 minutos.',
      },
      {
        titulo: 'Sentencias',
        icono: 'bi-bank',
        ruta: '/sentencias',
        pasos: [
          'Abre «Sentencias» en el menú.',
          'Describe la situación con tus palabras («proveedor que el SAT dice que no existe», «devolución negada»).',
          'Cada tesis del TFJA o de la SCJN dice a qué conclusión llegó el tribunal, a quién favoreció, qué conviene hacer, qué documentar y qué artículos interpreta.',
          'Una jurisprudencia es obligatoria; una tesis aislada o un precedente orientan. El portapapeles copia la cita.',
          'El equipo del despacho tiene el botón «Actualizar»: baja lo nuevo de los sitios oficiales y lo estudia; puede tardar.',
        ],
        nota: 'La conclusión y el «qué hacer» los resume Claude del texto publicado: para un escrito, cita la tesis y lee su texto completo.',
      },
    ],
  },
  {
    titulo: 'Actividad',
    temas: [
      {
        titulo: 'Ver quién hizo qué',
        icono: 'bi-clock-history',
        ruta: '/actividad',
        permisos: ['AuxiliarContable.Actividad'],
        texto: [
          'En Actividad está, día por día, lo que hizo cada quien: quién entró, a quién invitó y qué cambió en las organizaciones. Filtra por persona, por organización o por tipo.',
          'Lo repetido seguido (editar lo mismo varias veces) sale una vez con «×».',
          'Las acciones se guardan 30 días; las invitaciones aceptadas quedan siempre.',
        ],
      },
    ],
  },
  {
    titulo: 'Organizaciones',
    temas: [
      {
        titulo: 'Gestionar a la gente de tu organización',
        icono: 'bi-building',
        ruta: '/mi-organizacion',
        salvoPermisos: ['AuxiliarContable.Organizaciones'],
        texto: [
          'En Mi cuenta → Mi organización ves tu organización y tu papel en ella.',
          'Si eres su Administrador, ahí invitas a personas de tu empresa, cambias su papel o las quitas. Cada papel: Administrador (además gestiona a la gente), Miembro (sube y ve) y Solo ve.',
        ],
        nota: 'Una organización no puede quedarse sin administrador: nombra a otro antes de dejar de serlo.',
      },
      {
        titulo: 'Dar acceso a un cliente',
        icono: 'bi-building',
        ruta: '/organizaciones',
        permisos: ['AuxiliarContable.Organizaciones'],
        pasos: [
          'Ve a Organizaciones y crea una con el nombre del cliente (una persona física, una empresa o un grupo de empresas).',
          'Invita a la persona del cliente con el rol Usuario y elige su organización y su papel en ella en la misma invitación (o créala ahí con «Nueva»). Al aceptar, entra ya dentro.',
          'Nombra a una persona del cliente Administrador de su organización (en la lista de Personas): desde «Mi organización» invitará y gestionará a su propia gente, sin depender de ti.',
        ],
        nota: 'Una cuenta de cliente siempre se crea dentro de una organización: Invitaciones y Usuarios no dejan continuar sin elegirla. Quitar a alguien de la organización le corta el acceso al momento. El equipo del despacho (Owner y Administradores) ve todo lo de las organizaciones sin pertenecer a ninguna.',
      },
    ],
  },
  {
    titulo: 'Personas',
    temas: [
      {
        titulo: 'Invitar a alguien',
        icono: 'bi-envelope-plus',
        ruta: '/invitaciones',
        permisos: ['AbpIdentity.Users.Create'],
        pasos: [
          'Ve a Administración → Invitaciones (o «Invitar» en Usuarios).',
          'Elige el rol —solo aparecen los que están por debajo del tuyo— y escribe su correo.',
          'Pulsa «Enviar invitación». Le llega un enlace para crear su cuenta.',
          'Sigue el estado en la lista: Pendiente, Aceptada, Vencida o Revocada. «Reenviar» manda un enlace nuevo.',
        ],
        nota: 'Invitar es mejor que crear la cuenta a mano: no tienes que inventar contraseñas y el correo queda verificado.',
      },
      {
        titulo: 'Crear una cuenta sin correo',
        icono: 'bi-person-plus',
        ruta: '/identity/users',
        permisos: ['AbpIdentity.Users.Create'],
        pasos: [
          'En Usuarios pulsa «Crear a mano» y marca «Aún no tiene correo».',
          'Pon su usuario, su rol y una contraseña temporal, y pásasela en persona.',
          'Al entrar, la consola le pedirá registrar y verificar su correo y su teléfono.',
        ],
      },
      {
        titulo: 'Gestionar usuarios',
        icono: 'bi-person-lines-fill',
        ruta: '/identity/users',
        permisos: ['AbpIdentity.Users.Update'],
        texto: [
          'En Usuarios editas los datos de cada persona, su rol y su contraseña. Solo ves y tocas a quien está por debajo de ti.',
          'Para pausar el acceso de alguien sin perder su historial, desmarca «Activo» en vez de eliminarlo.',
        ],
      },
      {
        titulo: 'Si alguien no puede entrar',
        icono: 'bi-shield-x',
        ruta: '/identity/users',
        permisos: ['AbpIdentity.Users.Update'],
        pasos: [
          'Si olvidó la contraseña: en Usuarios, «Editar» y escríbele una nueva.',
          'Si tiene la verificación en dos pasos y no le llega el código ni tiene códigos de recuperación: al editarla, en «Seguridad», pulsa «Quitar verificación en dos pasos».',
        ],
        nota: 'Hazlo solo si confirmaste que es esa persona quien lo pide: es justo lo que intentaría quien quiere robarle la cuenta. Queda registrado.',
      },
      {
        titulo: 'Permisos de una persona',
        icono: 'bi-key',
        ruta: '/identity/users',
        permisos: ['AbpIdentity.Users.ManagePermissions'],
        texto: ['El botón de la llave en Usuarios da permisos a una sola persona, además de los de su rol. Úsalo para excepciones.'],
      },
    ],
  },
  {
    titulo: 'Roles y marca',
    temas: [
      {
        titulo: 'Roles y permisos',
        icono: 'bi-shield-lock',
        ruta: '/identity/roles',
        permisos: ['AbpIdentity.Roles.Create', 'AbpIdentity.Roles.ManagePermissions'],
        pasos: [
          'En Administración → Roles ves los roles que puedes gestionar.',
          'Para un puesto nuevo (por ejemplo «Analista» o «Supervisor»), crea un rol y elige su NIVEL: Usuario no gestiona a nadie; Administrador gestiona a los Usuarios; Owner, a Administradores y Usuarios.',
          'En «Opciones → Permisos» marca qué puede hacer.',
          'Asígnalo al invitar o al editar a una persona.',
        ],
        nota: 'Solo puedes crear roles de un nivel por debajo del tuyo y dar permisos que tú tienes. El nivel de los roles propios (Owner, Administrador, Usuario) no se cambia.',
      },
      {
        titulo: 'La marca',
        icono: 'bi-palette',
        ruta: '/branding',
        permisos: ['AuxiliarContable.Branding'],
        texto: ['En Sistema → Marca subes el logo y eliges el color principal. Se aplica a la consola y a la pantalla de entrar.'],
      },
    ],
  },
  {
    titulo: 'Operación',
    temas: [
      {
        titulo: 'Invisible para los demás',
        icono: 'bi-eye-slash',
        nivelMinimo: 3,
        texto: [
          'Tu cuenta no aparece en ninguna lista, ni tus auditorías, invitaciones o roles. Si alguien la pide por la API recibe «no existe».',
          'Las invitaciones que mandas no llevan tu nombre.',
        ],
      },
      {
        titulo: 'Estado, métricas y trabajos',
        icono: 'bi-hdd-stack',
        ruta: '/system-status',
        permisos: ['AuxiliarContable.SystemStatus', 'AuxiliarContable.Jobs', 'AuxiliarContable.Metrics'],
        texto: [
          'Sistema reúne Estado, Métricas, Auditoría, Alertas, Seguridad (IPs bloqueadas) y Trabajos en segundo plano.',
          'Si un correo no llega, mira Trabajos: el envío se reintenta solo y ahí queda el último error de Brevo.',
        ],
      },
    ],
  },
];

/** Qué puede hacer cada rol, tal como nacen (ver PermisosInicialesPorRol en el backend). */
export const MATRIZ: { accion: string; usuario: boolean; administrador: boolean; owner: boolean }[] = [
  { accion: 'Ver la información de su organización (según su papel en ella)', usuario: true, administrador: true, owner: true },
  { accion: 'Gestionar a la gente de su organización (si es su Administrador)', usuario: true, administrador: true, owner: true },
  { accion: 'Ver y gestionar a todos los clientes (organizaciones) y sus personas', usuario: false, administrador: true, owner: true },
  { accion: 'Ver la actividad (quién hizo qué)', usuario: false, administrador: true, owner: true },
  { accion: 'Invitar y gestionar Usuarios', usuario: false, administrador: true, owner: true },
  { accion: 'Invitar y gestionar Administradores', usuario: false, administrador: false, owner: true },
  { accion: 'Crear roles y decidir sus permisos', usuario: false, administrador: false, owner: true },
  { accion: 'Cambiar la marca (logo y color)', usuario: false, administrador: false, owner: true },
];

/** El nivel de quien tiene estos roles: el de su rol más alto. */
export function nivelDeRoles(roles: string[] | undefined | null): number {
  const niveles = (roles ?? []).map(r => {
    switch (r.toLowerCase()) {
      case 'admin':
        return 3;
      case 'owner':
        return 2;
      case 'administrador':
        return 1;
      default:
        return 0;
    }
  });
  return Math.max(0, ...niveles);
}

/** Las secciones con solo los temas que esta persona puede usar; las vacías desaparecen. */
export function manualPara(tienePermiso: (p: string) => boolean, nivel: number, chat = true): Seccion[] {
  return SECCIONES.map(s => ({
    ...s,
    temas: s.temas.filter(
      t =>
        (!t.permisos || t.permisos.some(tienePermiso)) &&
        !(t.salvoPermisos ?? []).some(tienePermiso) &&
        (t.nivelMinimo === undefined || nivel >= t.nivelMinimo) &&
        (chat || !t.chat),
    ),
  })).filter(s => s.temas.length > 0);
}
