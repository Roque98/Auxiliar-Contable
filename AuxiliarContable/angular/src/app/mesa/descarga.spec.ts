import { describe, expect, it } from 'vitest';
import { MAX_MESES_POR_ORDEN, haceCuanto, mesesDeLaOrden, textoDelPaquete } from './descarga';

describe('orden del Descargador 1', () => {
  it('cuenta los meses de la orden, cruzando el año', () => {
    expect(mesesDeLaOrden(2026, 3, 2026, 3)).toBe(1);
    expect(mesesDeLaOrden(2025, 11, 2026, 2)).toBe(4);
    expect(mesesDeLaOrden(2026, 5, 2026, 2)).toBeLessThan(1);
    expect(mesesDeLaOrden(2024, 1, 2026, 1)).toBeGreaterThan(MAX_MESES_POR_ORDEN);
  });
});

describe('resultado del paquete', () => {
  it('dice lo nuevo, lo repetido, lo cambiado y lo cancelado', () => {
    const texto = textoDelPaquete({
      desde: '2026-01', hasta: '2026-03', nuevas: 12, repetidas: 3, reemplazadas: 1, enMesCerrado: 0, rechazadas: 0,
      estatusPuestos: 15, canceladas: 2, sinXmlEnElSat: 0, avisos: [],
    });
    expect(texto).toBe('12 facturas nuevas · 3 ya estaban · 1 XML dañado cambiado por el bueno · estatus del SAT en 15 · 2 canceladas (2026-01 a 2026-03).');
  });

  it('con un solo mes y sin nada extra', () => {
    const texto = textoDelPaquete({
      desde: '2026-02', hasta: '2026-02', nuevas: 1, repetidas: 0, reemplazadas: 0, enMesCerrado: 0, rechazadas: 0,
      estatusPuestos: 1, canceladas: 0, sinXmlEnElSat: 0, avisos: [],
    });
    expect(texto).toBe('1 factura nueva · estatus del SAT en 1 (2026-02).');
  });
});

describe('cuándo se calculó', () => {
  const ahora = new Date('2026-11-17T18:00:00Z');
  it('en minutos u horas', () => {
    expect(haceCuanto('2026-11-17T17:59:30Z', ahora)).toBe('hace un momento');
    expect(haceCuanto('2026-11-17T17:57:00Z', ahora)).toBe('hace 3 min');
    expect(haceCuanto('2026-11-17T15:00:00Z', ahora)).toBe('hace 3 h');
    expect(haceCuanto('2026-11-14T18:00:00Z', ahora)).toMatch(/^el /);
  });
});
