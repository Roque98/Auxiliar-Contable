import { describe, expect, it } from 'vitest';
import { consejoDelMotivo, detalleDelRenglon, mesPorOmision, porGrupo, seccionContable } from './concentrado';

describe('hoja concentrado', () => {
  it('el renglón de deducciones del ISR es de enero al mes en flujo y del mes en RESICO', () => {
    expect(detalleDelRenglon('deducciones', 'isr', 'flujo')).toEqual({ detalle: 'deducciones', soloDelMes: false });
    expect(detalleDelRenglon('ingresos', 'isr', 'resico_pf')).toEqual({ detalle: 'ingresos', soloDelMes: true });
  });

  it('el IVA siempre es del mes y las retenciones dependen de la obligación', () => {
    expect(detalleDelRenglon('iva_acreditable', 'iva', 'flujo')).toEqual({ detalle: 'iva_acreditable', soloDelMes: true });
    expect(detalleDelRenglon('retenciones', 'iva', 'pm')).toEqual({ detalle: 'retenciones_iva', soloDelMes: true });
    expect(detalleDelRenglon('retenciones', 'isr', 'pm')).toEqual({ detalle: 'retenciones_isr', soloDelMes: false });
    expect(detalleDelRenglon(null, 'isr', 'pm')).toBeNull();
  });

  it('solo los requisitos traen solución; lo demás solo se explica', () => {
    expect(consejoDelMotivo('requisito', 'Pide que corrijan la forma de pago.').texto).toContain('corrijan');
    expect(consejoDelMotivo('indispensable', 'cámbialo').texto).not.toContain('cámbialo');
    expect(consejoDelMotivo('ley', 'x').texto).toContain('ley');
  });

  it('al entrar se ve el mes que toca declarar', () => {
    expect(mesPorOmision(new Date(2026, 10, 21))).toEqual({ anio: 2026, mes: 10 });
    expect(mesPorOmision(new Date(2027, 0, 5))).toEqual({ anio: 2026, mes: 12 });
  });

  it('cada pestaña contable pide su sección', () => {
    expect(seccionContable('deudas_contables', 'fiscales')).toBe('deudas_contables');
    expect(seccionContable('ingresos', 'contables')).toBe('ingresos_contables');
    expect(seccionContable('ingresos', 'fiscales')).toBeNull();
    expect(seccionContable('deudas_fiscales', 'fiscales')).toBeNull();
  });

  it('los rubros se agrupan por su grupo de la NIF B-6 con su total', () => {
    const g = porGrupo([
      { grupo: 'Pasivo a corto plazo', saldo: 100.1 }, { grupo: 'Pasivo a largo plazo', saldo: 5 }, { grupo: 'Pasivo a corto plazo', saldo: 0.2 },
    ]);
    expect(g.map(x => [x.grupo, x.total, x.rubros.length])).toEqual([['Pasivo a corto plazo', 100.3, 2], ['Pasivo a largo plazo', 5, 1]]);
  });
});
