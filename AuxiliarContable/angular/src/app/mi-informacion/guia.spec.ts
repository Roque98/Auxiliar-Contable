import { describe, expect, it } from 'vitest';
import { TipoDeDocumento } from '../documentos/tipos';
import {
  EstadoDePata,
  FacturaDeLaOperacion,
  FormaDeTransaccion,
  OperacionMinima,
  SentidoDeOperacion,
  contestada,
  prometida,
  siguientePregunta,
} from './guia';

const pendiente = { estado: EstadoDePata.Pendiente };
const ligada = { estado: EstadoDePata.Ligada, documentoId: 'd1' };

function op(p: Partial<OperacionMinima> = {}): OperacionMinima {
  return { sentido: SentidoDeOperacion.Salida, importe: 1160, factura: ligada, transaccion: pendiente, controlInterno: pendiente, ...p };
}

function factura(p: Partial<FacturaDeLaOperacion> = {}): FacturaDeLaOperacion {
  return { tipo: 'I', metodo: 'PUE', forma: '03', total: 1160, moneda: 'MXN', tipoRelacion: null, uuidRelacionado: null, ...p };
}

describe('guía del trabajo diario', () => {
  it('con factura subida pregunta primero por la transacción y luego por el control interno', () => {
    const p = siguientePregunta(op(), factura())!;
    expect(p.pata).toBe('transaccion');
    expect(p.texto).toContain('comprobante de la transacción');
    const textos = p.opciones.map(o => o.texto);
    expect(textos).toEqual(['Sí, subo el comprobante ahora', 'Viene en el estado de cuenta', 'Lo pagué en efectivo', 'Lo subiré después', 'No lo subiré']);
    expect(p.opciones[0].accion).toMatchObject({ tipo: 'subir', documento: TipoDeDocumento.ComprobanteDePago });
    expect(p.opciones[4].advertencia).toContain('impuestos');

    const despues = siguientePregunta(op({ transaccion: { estado: EstadoDePata.Pendiente, motivo: 'Subiré el comprobante después' } }), factura())!;
    expect(despues.pata).toBe('controlInterno');
    expect(despues.texto).toContain('ticket o comprobante que te dieron');
  });

  it('si la factura dice efectivo pero dicen que lo pagaron en efectivo y es otra forma, advierte', () => {
    const p = siguientePregunta(op(), factura({ forma: '03' }))!;
    expect(p.opciones.find(o => o.texto === 'Lo pagué en efectivo')!.advertencia).toContain('01 Efectivo');
  });

  it('factura en efectivo: se corrobora, y un gasto de más de $2,000 avisa que no es deducible', () => {
    const p = siguientePregunta(op({ importe: 5000 }), factura({ forma: '01', total: 5000 }))!;
    expect(p.texto).toContain('en efectivo');
    expect(p.opciones[0].accion).toMatchObject({ tipo: 'guardar', respuesta: { forma: FormaDeTransaccion.Efectivo } });
    expect(p.opciones[0].advertencia).toContain('27');
  });

  it('dación en pago: no pide comprobante, confirma que no hubo dinero', () => {
    const p = siguientePregunta(op(), factura({ forma: '12' }))!;
    expect(p.texto).toContain('Dación en pago');
    expect(p.texto).toContain('no hubo movimiento');
    expect(p.opciones[0].accion).toMatchObject({ tipo: 'guardar', respuesta: { estado: EstadoDePata.NoHay } });
    expect(p.opciones.some(o => o.accion.tipo === 'subir')).toBe(false);
    const no = p.opciones[1].accion;
    expect(no.tipo).toBe('preguntar');
  });

  it('PPD: confirma que no se ha pagado; si ya se pagó, pide el complemento o PUE', () => {
    const p = siguientePregunta(op({ sentido: SentidoDeOperacion.Entrada }), factura({ metodo: 'PPD', forma: '99' }))!;
    expect(p.texto).toContain('PPD');
    expect(p.texto).toContain('aún no se cobró');
    expect(p.opciones[0].accion).toMatchObject({ tipo: 'guardar', respuesta: { forma: FormaDeTransaccion.Credito } });
    const ya = p.opciones[1].accion;
    expect(ya.tipo).toBe('preguntar');
    if (ya.tipo === 'preguntar') {
      expect(ya.pregunta.texto).toContain('PUE o falta el complemento de pago');
      expect(ya.pregunta.opciones[0].accion).toMatchObject({ tipo: 'subir', documento: TipoDeDocumento.Cfdi });
    }
  });

  it('nota de crédito y nómina tienen sus propias preguntas', () => {
    const e = siguientePregunta(op(), factura({ tipo: 'E', uuidRelacionado: 'ABCDEF12-0000-0000-0000-000000000000' }))!;
    expect(e.nota).toContain('devolución, descuento o bonificación');
    expect(e.opciones.map(o => o.texto)).toContain('No hubo dinero: se descontó de lo que se debía');
    const n = siguientePregunta(op(), factura({ tipo: 'N' }))!;
    expect(n.texto).toContain('nómina');
    const control = siguientePregunta(op({ transaccion: ligada }), factura({ tipo: 'N' }))!;
    expect(control.opciones[0].accion).toMatchObject({ tipo: 'subir', documento: TipoDeDocumento.Nomina });
  });

  it('si lo primero fue el comprobante o el ticket, pregunta por la factura con sus motivos', () => {
    const p = siguientePregunta(op({ factura: pendiente, transaccion: ligada }), null)!;
    expect(p.pata).toBe('factura');
    expect(p.opciones.map(o => o.texto)).toEqual([
      'Sí, la subo ahora (XML)',
      'Ya le pedí al contador que la hiciera',
      'La subiré después: todavía no la hago',
      'La subiré después: no me la han mandado',
      'No habrá factura',
    ]);
    // Sin factura, la transacción ofrece también «a crédito».
    const t = siguientePregunta(op({ factura: { estado: EstadoDePata.NoHay, motivo: 'No habrá factura' }, controlInterno: ligada }), null)!;
    expect(t.opciones.map(o => o.texto)).toContain('Aún no se paga (a crédito)');
  });

  it('todo contestado: no hay más preguntas; lo prometido se recuerda', () => {
    const lista = op({ transaccion: ligada, controlInterno: { estado: EstadoDePata.Pendiente, motivo: 'Lo subiré después' } });
    expect(siguientePregunta(lista, factura())).toBeNull();
    expect(contestada(lista.controlInterno)).toBe(true);
    expect(prometida(lista.controlInterno)).toBe(true);
    expect(prometida({ estado: EstadoDePata.Pendiente, motivo: 'Se amarra con el estado de cuenta al conciliar.' })).toBe(false);
  });
});
