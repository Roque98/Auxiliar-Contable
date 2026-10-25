/**
 * El aviso de privacidad. ⚠️ Al cambiar el TEXTO hay que cambiar `VERSION_DEL_AVISO` y, con el
 * mismo valor, `AvisoDePrivacidadConsts.VersionVigente` en el backend: todos tendrán que
 * aceptarlo de nuevo. Un cambio de redacción sin cambio de versión deja a la gente «aceptando»
 * un texto que no leyó.
 *
 * Borrador técnico basado en la LFPDPPP: lo tiene que revisar un abogado antes de usarse con
 * clientes reales. Lo marcado con `PENDIENTE` son datos del despacho que faltan.
 */
export const VERSION_DEL_AVISO = '2026-10-25';

/** Quién responde de los datos: el despacho, no la plataforma. */
export const RESPONSABLE = {
  nombre: 'PENDIENTE: razón social del despacho',
  domicilio: 'PENDIENTE: domicilio del despacho',
  correo: 'PENDIENTE: correo para privacidad y derechos ARCO',
};

export const FALTAN_DATOS_DEL_RESPONSABLE = Object.values(RESPONSABLE).some(v => v.startsWith('PENDIENTE'));

export interface SeccionDelAviso {
  titulo: string;
  parrafos?: string[];
  lista?: string[];
  /** Párrafos que van DESPUÉS de la lista. */
  notas?: string[];
}

export const SECCIONES: SeccionDelAviso[] = [
  {
    titulo: 'Quién es responsable de tus datos',
    parrafos: [
      `${RESPONSABLE.nombre}, con domicilio en ${RESPONSABLE.domicilio}, es responsable del tratamiento de tus datos personales, conforme a la Ley Federal de Protección de Datos Personales en Posesión de los Particulares. Para cualquier asunto de privacidad escríbenos a ${RESPONSABLE.correo}.`,
    ],
  },
  {
    titulo: 'Qué datos tratamos',
    lista: [
      'De identificación y contacto: nombre, correo electrónico, teléfono y usuario.',
      'Fiscales: RFC, CURP, régimen fiscal, obligaciones, actividades económicas, domicilio fiscal y lo que contenga tu Constancia de Situación Fiscal.',
      'Financieros y patrimoniales: estados de cuenta, movimientos bancarios, facturas (CFDI) emitidas y recibidas, tickets y otros comprobantes que subas.',
      'De uso de la plataforma: inicios de sesión, dirección IP, acciones realizadas y conversaciones con el asistente.',
    ],
    notas: [
      'No pedimos datos sensibles (salud, origen étnico, creencias, etc.). Si alguno aparece en un documento que subas, se trata solo como parte de ese documento.',
    ],
  },
  {
    titulo: 'Para qué los usamos',
    parrafos: ['Finalidades necesarias para darte el servicio:'],
    lista: [
      'Organizar, almacenar y clasificar tu información contable y fiscal.',
      'Conciliar tus facturas con tus estados de cuenta y detectar faltantes o inconsistencias.',
      'Preparar la información que el despacho necesita para atender tus obligaciones fiscales.',
      'Darte acceso seguro a la plataforma y avisarte de lo que requiera tu atención.',
      'Cumplir obligaciones legales, incluida la conservación de la contabilidad por los plazos que marca la ley.',
    ],
  },
  {
    titulo: 'Inteligencia artificial',
    parrafos: [
      'La plataforma usa un asistente de inteligencia artificial para leer documentos y conversar contigo. Sus propuestas (clasificaciones, datos extraídos) se confirman por ti o por el despacho antes de darse por buenas; el asistente no sustituye la revisión ni la firma de un contador.',
      'Para ello, el contenido estrictamente necesario de tus documentos se envía a nuestro proveedor de inteligencia artificial, Anthropic (Estados Unidos), que actúa como encargado: lo procesa solo para darnos la respuesta y, según sus condiciones comerciales, no lo usa para entrenar sus modelos.',
    ],
  },
  {
    titulo: 'Con quién los compartimos',
    parrafos: [
      'Usamos proveedores que tratan tus datos por cuenta nuestra (encargados), con las mismas obligaciones de confidencialidad: alojamiento del servidor, protección de red (Cloudflare), envío de correos (Brevo), almacenamiento de copias de seguridad cifradas y el proveedor de inteligencia artificial mencionado. Algunos están fuera de México.',
      'No vendemos ni cedemos tus datos. Solo los entregaríamos a una autoridad cuando una ley o una orden lo exija.',
    ],
  },
  {
    titulo: 'Tus derechos (ARCO) y cómo revocar tu consentimiento',
    parrafos: [
      `Puedes acceder a tus datos, rectificarlos, cancelarlos u oponerte a su tratamiento, así como revocar tu consentimiento, escribiendo a ${RESPONSABLE.correo} con tu nombre, el derecho que quieres ejercer y una identificación. Te responderemos en un máximo de 20 días hábiles.`,
      'La cancelación no alcanza a la información que la ley nos obliga a conservar (por ejemplo, la contabilidad durante cinco años); esa se bloquea y se elimina al terminar el plazo.',
    ],
  },
  {
    titulo: 'Cómo los protegemos',
    parrafos: [
      'Las conexiones van cifradas, cada cliente solo ve su propia información, el acceso se registra y las copias de seguridad se guardan cifradas fuera del servidor.',
    ],
  },
  {
    titulo: 'Cambios a este aviso',
    parrafos: [
      'Si este aviso cambia, te lo mostraremos al entrar a la plataforma y tendrás que aceptarlo de nuevo para seguir usándola.',
    ],
  },
];
