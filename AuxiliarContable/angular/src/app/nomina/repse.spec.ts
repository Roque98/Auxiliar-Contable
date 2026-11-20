import { describe, expect, it } from 'vitest';
import { estadoDelContrato, rfcValido } from './repse';
import type { ContratoDelMesDto } from './repse.service';

function c(p: Partial<ContratoDelMesDto> = {}): ContratoDelMesDto {
  return {
    contratoId: '1', rol: 'recibe', contraparte: 'Prueba', facturas: 1, importe: 1160, limite: '2026-12-31', vencido: false,
    casillas: [{ clave: 'iva', texto: 'IVA', fundamento: 'art. 5-II LIVA', cumplida: false }], ...p,
  };
}

describe('servicios especializados', () => {
  it('el estado del contrato en el mes', () => {
    expect(estadoDelContrato(c())).toBe('pendiente');
    expect(estadoDelContrato(c({ vencido: true }))).toBe('vencido');
    expect(estadoDelContrato(c({ facturas: 0 }))).toBe('sin-facturas');
    expect(estadoDelContrato(c({ casillas: [{ clave: 'iva', texto: '', fundamento: '', cumplida: true }] }))).toBe('completo');
  });

  it('RFC de 12 o 13 caracteres', () => {
    expect(rfcValido('AAA010101AAA')).toBe(true);
    expect(rfcValido('gode561231gr8')).toBe(true);
    expect(rfcValido('AAA0101')).toBe(false);
  });
});
