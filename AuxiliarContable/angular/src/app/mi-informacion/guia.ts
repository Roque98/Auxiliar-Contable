// La guía del trabajo diario: con lo primero que se subió de una operación (y, si hay factura, con lo que dice su XML)
// decide la siguiente pregunta para completar las tres patas —factura, transacción y control interno— o saber por qué
// alguna no va a llegar. Puro: sin Angular, se prueba con vitest.
import { TipoDeDocumento } from '../documentos/tipos';

/** Igual que en operaciones.service (sin importarlo, para no arrastrar Angular a las pruebas). */
export enum EstadoDePata {
  Pendiente = 0,
  Ligada = 1,
  NoHay = 2,
}

export enum FormaDeTransaccion {
  Banco = 0,
  Efectivo = 1,
  Credito = 2,
}

export enum SentidoDeOperacion {
  Salida = 0,
  Entrada = 1,
}

export type Pata = 'factura' | 'transaccion' | 'controlInterno';

export interface PataMinima {
  estado: EstadoDePata;
  motivo?: string | null;
  documentoId?: string | null;
  /** Lo que leyó Claude del documento de la pata: si es devolución, descuento o bonificación. */
  analisis?: { ajuste?: string | null } | null;
}

const NOMBRES_DE_AJUSTE: Record<string, string> = { devolucion: 'una devolución', descuento: 'un descuento', bonificacion: 'una bonificación' };

/** Devolución, descuento o bonificación que se ve en el comprobante o el ticket ya subidos (null si no). */
export function ajusteDe(op: OperacionMinima): string | null {
  const a = op.transaccion.analisis?.ajuste ?? op.controlInterno.analisis?.ajuste;
  return a && a in NOMBRES_DE_AJUSTE ? a : null;
}

export interface OperacionMinima {
  sentido: SentidoDeOperacion;
  importe: number | null;
  factura: PataMinima;
  transaccion: PataMinima;
  controlInterno: PataMinima;
}

/** Lo que dice el XML de la factura de la operación (`OperacionesAppService.GetFactura`). */
export interface FacturaDeLaOperacion {
  /** I ingreso, E egreso (nota de crédito), P pago (complemento), N nómina, T traslado. */
  tipo: string | null;
  /** PUE o PPD. */
  metodo: string | null;
  /** Clave del catálogo c_FormaPago (01 efectivo, 03 transferencia, 12 dación en pago…). */
  forma: string | null;
  total: number | null;
  moneda: string | null;
  /** 01 nota de crédito, 03 devolución, 07 aplicación de anticipo… */
  tipoRelacion: string | null;
  uuidRelacionado: string | null;
}

/** Lo que guarda una respuesta (igual que `GuardarPataDto`). */
export interface RespuestaDePata {
  estado: EstadoDePata;
  motivo?: string | null;
  forma?: FormaDeTransaccion | null;
}

export type Accion =
  | { tipo: 'subir'; pata: Pata; documento: TipoDeDocumento; acepta: string }
  | { tipo: 'guardar'; pata: Pata; respuesta: RespuestaDePata }
  | { tipo: 'preguntar'; pregunta: Pregunta };

export interface Opcion {
  texto: string;
  accion: Accion;
  /** Si trae advertencia, se enseña y se pide confirmar antes de hacer la acción. */
  advertencia?: string;
  principal?: boolean;
}

export interface Pregunta {
  pata: Pata;
  titulo: string;
  texto: string;
  /** Lo que se detectó y por qué se pregunta así (forma de pago, PPD, nómina…). */
  nota?: string;
  opciones: Opcion[];
}

export const FORMAS_DE_PAGO: Record<string, string> = {
  '01': 'Efectivo', '02': 'Cheque nominativo', '03': 'Transferencia electrónica de fondos', '04': 'Tarjeta de crédito',
  '05': 'Monedero electrónico', '06': 'Dinero electrónico', '08': 'Vales de despensa', '12': 'Dación en pago',
  '13': 'Pago por subrogación', '14': 'Pago por consignación', '15': 'Condonación', '17': 'Compensación', '23': 'Novación',
  '24': 'Confusión', '25': 'Remisión de deuda', '26': 'Prescripción o caducidad', '27': 'A satisfacción del acreedor',
  '28': 'Tarjeta de débito', '29': 'Tarjeta de servicios', '30': 'Aplicación de anticipos', '31': 'Intermediario pagos',
  '99': 'Por definir',
};

/** Formas de pago en las que no se mueve dinero (ni banco ni efectivo) al momento de la factura. */
export const FORMAS_SIN_DINERO = ['12', '13', '14', '15', '17', '23', '24', '25', '26', '27', '30'];

const FOTO_O_PDF = '.pdf,.jpg,.jpeg,.png,.webp,application/pdf,image/*';
const XML = '.xml,application/xml,text/xml';

export const ADVERTENCIA_SIN_DOCUMENTO =
  'Sin este documento la operación queda menos sustentada: puede cambiar cómo se registra en tu contabilidad y en el ' +
  'cálculo de tus impuestos (deducción del gasto, acreditamiento del IVA o la prueba ante el SAT de que la operación existió).';

const ESTADO_DE_CUENTA = 'Se amarra con el estado de cuenta al conciliar';

/** ¿La pata ya tiene respuesta? Pendiente con motivo = «lo subiré después» (ya contestó; se le recuerda). */
export function contestada(p: PataMinima): boolean {
  return p.estado !== EstadoDePata.Pendiente || !!p.motivo?.trim();
}

/** ¿Es una pata que quedó «para después» y hay que recordarla? (El amarre con el estado de cuenta no se recuerda.) */
export function prometida(p: PataMinima): boolean {
  return p.estado === EstadoDePata.Pendiente && !!p.motivo?.trim() && !p.motivo.startsWith(ESTADO_DE_CUENTA);
}

const guardar = (pata: Pata, respuesta: RespuestaDePata): Accion => ({ tipo: 'guardar', pata, respuesta });
const despues = (pata: Pata, motivo: string): Accion => guardar(pata, { estado: EstadoDePata.Pendiente, motivo });
const noHay = (pata: Pata, motivo: string): Accion => guardar(pata, { estado: EstadoDePata.NoHay, motivo });

const dinero = (n: number | null) => (n == null ? '' : ` por ${n.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' })}`);

/** La siguiente pregunta para la operación, o null si ya están contestadas las tres patas. Orden: factura, transacción, control interno. */
export function siguientePregunta(op: OperacionMinima, f: FacturaDeLaOperacion | null): Pregunta | null {
  if (!contestada(op.factura)) {
    return preguntaDeFactura(op);
  }
  if (!contestada(op.transaccion)) {
    return preguntaDeTransaccion(op, op.factura.estado === EstadoDePata.Ligada ? f : null);
  }
  if (!contestada(op.controlInterno)) {
    return preguntaDeControl(op, op.factura.estado === EstadoDePata.Ligada ? f : null);
  }
  return null;
}

export function preguntaDeFactura(op: OperacionMinima): Pregunta {
  const venta = op.sentido === SentidoDeOperacion.Entrada;
  const ajuste = ajusteDe(op);
  if (ajuste) {
    // El dinero va al revés: si al cliente le devuelven (entrada), la nota la emite el proveedor; si él devuelve, la emite él.
    const laEmiteElCliente = op.sentido === SentidoDeOperacion.Salida;
    return {
      pata: 'factura',
      titulo: 'Nota de crédito',
      texto: `Esto es ${NOMBRES_DE_AJUSTE[ajuste]}. ¿Hay nota de crédito (CFDI de egreso) relacionada con la factura original?`,
      nota: laEmiteElCliente ? 'La nota de crédito la emites tú a tu cliente.' : 'La nota de crédito te la tiene que dar tu proveedor.',
      opciones: [
        { texto: 'Sí, la subo ahora (XML)', principal: true, accion: { tipo: 'subir', pata: 'factura', documento: TipoDeDocumento.Cfdi, acepta: XML } },
        { texto: laEmiteElCliente ? 'Ya le pedí al contador que la hiciera' : 'Ya se la pedí al proveedor', accion: despues('factura', laEmiteElCliente ? 'Ya le pedí al contador la nota de crédito' : 'Ya pidió la nota de crédito al proveedor') },
        { texto: 'La subiré después', accion: despues('factura', 'Subirá la nota de crédito después') },
        {
          texto: 'No habrá nota de crédito',
          accion: noHay('factura', 'No habrá nota de crédito'),
          advertencia: 'Sin nota de crédito no se puede ajustar el IVA (art. 7 LIVA): '
            + (laEmiteElCliente ? 'seguirás pagando el IVA de la venta original.' : 'tendrías que disminuir tu IVA acreditable de todos modos.'),
        },
      ],
    };
  }
  return {
    pata: 'factura',
    titulo: 'Factura',
    texto: venta ? '¿Vas a subir la factura de esta venta?' : '¿Vas a subir la factura de esta compra o gasto?',
    opciones: [
      { texto: 'Sí, la subo ahora (XML)', principal: true, accion: { tipo: 'subir', pata: 'factura', documento: TipoDeDocumento.Cfdi, acepta: XML } },
      { texto: 'Ya le pedí al contador que la hiciera', accion: despues('factura', 'Ya le pedí al contador que hiciera la factura') },
      { texto: 'La subiré después: todavía no la hago', accion: despues('factura', 'La subiré después: todavía no la hago') },
      { texto: 'La subiré después: no me la han mandado', accion: despues('factura', 'La subiré después: no me la han mandado') },
      {
        texto: 'No habrá factura',
        accion: noHay('factura', 'No habrá factura'),
        advertencia: venta
          ? 'Aunque no factures, el ingreso se acumula igual para tus impuestos (y puede ir en la factura global al público en general).'
          : 'Sin factura, el gasto no es deducible y su IVA no se puede acreditar.',
      },
    ],
  };
}

/** Las opciones de siempre para una transacción por banco: el comprobante, el estado de cuenta, efectivo, después o no. */
function opcionesDeBanco(op: OperacionMinima, f: FacturaDeLaOperacion | null): Opcion[] {
  const efectivoContraFactura = f?.forma && f.forma !== '01' && f.forma !== '99'
    ? `La factura dice forma de pago «${FORMAS_DE_PAGO[f.forma] ?? f.forma}» (${f.forma}). Si en realidad fue en efectivo, la factura debería decir 01 Efectivo: pide que la corrijan.`
    : undefined;
  const opciones: Opcion[] = [
    { texto: 'Sí, subo el comprobante ahora', principal: true, accion: { tipo: 'subir', pata: 'transaccion', documento: TipoDeDocumento.ComprobanteDePago, acepta: FOTO_O_PDF } },
    { texto: 'Viene en el estado de cuenta', accion: despues('transaccion', `${ESTADO_DE_CUENTA}.`) },
    { texto: 'Lo pagué en efectivo', accion: guardar('transaccion', { estado: EstadoDePata.Ligada, forma: FormaDeTransaccion.Efectivo }), advertencia: efectivoContraFactura ?? avisoDeEfectivo(op, f) },
    { texto: 'Lo subiré después', accion: despues('transaccion', 'Subiré el comprobante después') },
    { texto: 'No lo subiré', accion: noHay('transaccion', 'No subirá el comprobante de la transacción'), advertencia: ADVERTENCIA_SIN_DOCUMENTO },
  ];
  if (!f) {
    opciones.splice(3, 0, { texto: 'Aún no se paga (a crédito)', accion: guardar('transaccion', { estado: EstadoDePata.Ligada, forma: FormaDeTransaccion.Credito }) });
  }
  return opciones;
}

/** Compras de más de $2,000 pagadas en efectivo no son deducibles (art. 27, fracción III, LISR). */
function avisoDeEfectivo(op: OperacionMinima, f: FacturaDeLaOperacion | null): string | undefined {
  const total = f?.total ?? op.importe;
  return op.sentido === SentidoDeOperacion.Salida && total != null && total > 2000
    ? 'Un gasto de más de $2,000 pagado en efectivo no es deducible (art. 27, fracción III, LISR); en combustibles, ningún pago en efectivo lo es.'
    : undefined;
}

export function preguntaDeTransaccion(op: OperacionMinima, f: FacturaDeLaOperacion | null): Pregunta {
  const venta = op.sentido === SentidoDeOperacion.Entrada;
  const verbo = venta ? 'cobró' : 'pagó';
  const base = { pata: 'transaccion' as const, titulo: 'Transacción' };

  if (f?.tipo === 'N') {
    return {
      ...base,
      texto: 'Es un recibo de nómina. ¿Cómo se le pagó al trabajador?',
      nota: 'Lo de la nómina (cálculo, ISR y cuotas) lo ves en «Nómina»; aquí solo se amarra el pago.',
      opciones: [
        { texto: 'Por transferencia: subo el comprobante', principal: true, accion: { tipo: 'subir', pata: 'transaccion', documento: TipoDeDocumento.ComprobanteDePago, acepta: FOTO_O_PDF } },
        { texto: 'Por transferencia: viene en el estado de cuenta', accion: despues('transaccion', `${ESTADO_DE_CUENTA}.`) },
        { texto: 'En efectivo, con recibo firmado', accion: guardar('transaccion', { estado: EstadoDePata.Ligada, forma: FormaDeTransaccion.Efectivo }) },
        { texto: 'Lo subiré después', accion: despues('transaccion', 'Subiré el comprobante del pago de nómina después') },
      ],
    };
  }

  if (f?.tipo === 'E') {
    return {
      ...base,
      texto: '¿Cómo se aplicó esta nota de crédito?',
      nota: `Es una factura de egreso: una devolución, descuento o bonificación${f.uuidRelacionado ? ` de la factura ${f.uuidRelacionado.slice(0, 8)}…` : ''}. Puede haber regreso de dinero o solo bajar lo que se debía.`,
      opciones: [
        { texto: venta ? 'Devolví el dinero: subo el comprobante' : 'Me devolvieron el dinero: subo el comprobante', principal: true, accion: { tipo: 'subir', pata: 'transaccion', documento: TipoDeDocumento.ComprobanteDePago, acepta: FOTO_O_PDF } },
        { texto: 'La devolución viene en el estado de cuenta', accion: despues('transaccion', `${ESTADO_DE_CUENTA}.`) },
        { texto: 'Se devolvió en efectivo', accion: guardar('transaccion', { estado: EstadoDePata.Ligada, forma: FormaDeTransaccion.Efectivo }) },
        { texto: 'No hubo dinero: se descontó de lo que se debía', accion: noHay('transaccion', 'Nota de crédito aplicada contra el saldo de la factura relacionada: sin movimiento de dinero') },
      ],
    };
  }

  if (f?.tipo === 'P') {
    return {
      ...base,
      texto: `Es un complemento de pago: confirma que se ${verbo} una factura en parcialidades. ¿Subes el comprobante?`,
      opciones: opcionesDeBanco(op, f),
    };
  }

  if (f?.forma && FORMAS_SIN_DINERO.includes(f.forma)) {
    const nombre = FORMAS_DE_PAGO[f.forma] ?? f.forma;
    return {
      ...base,
      texto: `Esta factura dice que se pagó con «${nombre}». Por eso no hubo movimiento en el estado de cuenta ni en efectivo, ¿cierto?`,
      nota: `Forma de pago ${f.forma} (${nombre}): no es un medio bancario ni efectivo.`,
      opciones: [
        { texto: 'Sí, así fue', principal: true, accion: noHay('transaccion', `Forma de pago ${f.forma} ${nombre}: sin movimiento de dinero`) },
        {
          texto: 'No, sí se pagó con dinero',
          accion: {
            tipo: 'preguntar',
            pregunta: {
              ...base,
              texto: '¿Vas a subir el comprobante de la transacción?',
              nota: `Si se pagó con dinero, la factura debería decir la forma real (transferencia, efectivo…), no «${nombre}». Pide que la corrijan.`,
              opciones: opcionesDeBanco(op, f),
            },
          },
        },
      ],
    };
  }

  if (f?.metodo === 'PPD') {
    return {
      ...base,
      texto: `La factura está en PPD (pago en parcialidades o diferido). ¿Me confirmas que aún no se ${verbo}?`,
      nota: 'Una factura PPD se paga después y cada pago lleva su complemento de pago.',
      opciones: [
        { texto: `Correcto, aún no se ${verbo}`, principal: true, accion: guardar('transaccion', { estado: EstadoDePata.Ligada, forma: FormaDeTransaccion.Credito }) },
        {
          texto: `No, ya se ${verbo}`,
          accion: {
            tipo: 'preguntar',
            pregunta: {
              ...base,
              texto: `Si ya se ${verbo}, la factura tuvo que ser PUE o falta el complemento de pago. ¿Qué pasó?`,
              nota: 'Sin complemento, el pago de una PPD no cuenta en los impuestos del mes en que se hizo.',
              opciones: [
                { texto: 'Subo el complemento de pago (XML)', principal: true, accion: { tipo: 'subir', pata: 'transaccion', documento: TipoDeDocumento.Cfdi, acepta: XML } },
                { texto: venta ? 'Emitiré el complemento después' : 'Pediré el complemento al proveedor', accion: despues('transaccion', 'Falta el complemento de pago de la factura PPD') },
                { texto: 'La factura se va a cancelar y rehacer en PUE', accion: despues('transaccion', 'Factura PPD por rehacer en PUE') },
              ],
            },
          },
        },
      ],
    };
  }

  if (f?.forma === '01') {
    return {
      ...base,
      texto: `La factura dice que se ${verbo} en efectivo${dinero(f.total)}. ¿Así fue?`,
      opciones: [
        { texto: 'Sí, en efectivo', principal: true, accion: guardar('transaccion', { estado: EstadoDePata.Ligada, forma: FormaDeTransaccion.Efectivo }), advertencia: avisoDeEfectivo(op, f) },
        {
          texto: 'No, fue por transferencia o tarjeta',
          accion: {
            tipo: 'preguntar',
            pregunta: { ...base, texto: '¿Vas a subir el comprobante de la transacción?', nota: 'La factura debería decir la forma real de pago; pide que la corrijan.', opciones: opcionesDeBanco(op, f) },
          },
        },
      ],
    };
  }

  return {
    ...base,
    texto: '¿Vas a subir el comprobante de la transacción?',
    nota: f?.forma ? `La factura dice forma de pago ${f.forma} (${FORMAS_DE_PAGO[f.forma] ?? 'otra'}).` : undefined,
    opciones: opcionesDeBanco(op, f),
  };
}

export function preguntaDeControl(op: OperacionMinima, f: FacturaDeLaOperacion | null): Pregunta {
  const base = { pata: 'controlInterno' as const, titulo: 'Control interno' };
  if (f?.tipo === 'N') {
    return {
      ...base,
      texto: '¿Subes el recibo firmado o la lista de raya?',
      opciones: [
        { texto: 'Sí, lo subo ahora', principal: true, accion: { tipo: 'subir', pata: 'controlInterno', documento: TipoDeDocumento.Nomina, acepta: FOTO_O_PDF } },
        { texto: 'Lo subiré después', accion: despues('controlInterno', 'Subiré el recibo firmado después') },
        { texto: 'No lo voy a subir', accion: noHay('controlInterno', 'No subirá el recibo firmado'), advertencia: ADVERTENCIA_SIN_DOCUMENTO },
      ],
    };
  }
  const venta = op.sentido === SentidoDeOperacion.Entrada;
  const que = f?.tipo === 'E'
    ? 'la nota o vale de la devolución, descuento o bonificación'
    : venta ? 'la nota de venta que generaste' : 'el ticket o comprobante que te dieron';
  return {
    ...base,
    texto: `¿Vas a subir ${que}?`,
    opciones: [
      { texto: 'Sí, lo subo ahora (foto o PDF)', principal: true, accion: { tipo: 'subir', pata: 'controlInterno', documento: TipoDeDocumento.ControlInterno, acepta: FOTO_O_PDF } },
      { texto: 'Lo subiré después', accion: despues('controlInterno', 'Lo subiré después') },
      { texto: 'No lo voy a subir', accion: noHay('controlInterno', 'No subirá el comprobante interno'), advertencia: ADVERTENCIA_SIN_DOCUMENTO },
    ],
  };
}
