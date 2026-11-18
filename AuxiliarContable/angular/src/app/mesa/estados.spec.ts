import { describe, expect, it } from 'vitest';
import { EstadosFinancierosDto, mesLargo, renglonesDeResultado, renglonesDeSituacion } from './estados';

const ef: EstadosFinancierosDto = {
  desde: '2026-01', hasta: '2026-10', cuadre: 0, preliminar: false, advertencias: [], razones: [],
  resultado: [
    { concepto: 'Ventas o ingresos', importe: 5000, fuente: 'B-3 52.1.1 a)' },
    { concepto: '(−) Devoluciones, descuentos y bonificaciones', importe: 0, fuente: '' },
    { concepto: '= Utilidad bruta', importe: 5000, fuente: 'B-3 53.2.2.1 (opcional)' },
    { concepto: '= Utilidad (pérdida) neta', importe: 5000, fuente: 'B-3 52.1.1 i)' },
  ],
  situacion: [
    { concepto: 'ACTIVO', importe: null, fuente: '', tipo: 'titulo' },
    { concepto: 'Efectivo', importe: 100, fuente: 'B-6 52.9 a)', tipo: 'rubro' },
    { concepto: 'Total del activo', importe: 100, fuente: '', tipo: 'total' },
  ],
};

describe('estados financieros en pantalla', () => {
  it('quita los renglones en cero del de resultados y marca la utilidad neta', () => {
    const r = renglonesDeResultado(ef);
    expect(r.map(x => x.concepto)).toEqual(['Ventas o ingresos', '= Utilidad bruta', '= Utilidad (pérdida) neta']);
    expect(r[0].fundamento).toBe('NIF B-3 52.1.1 a)');
    expect(r.map(x => x.tipo)).toEqual(['renglon', 'subtotal', 'resultado']);
  });

  it('el de situación financiera conserva títulos y totales', () => {
    expect(renglonesDeSituacion(ef).map(x => x.tipo)).toEqual(['titulo', 'renglon', 'resultado']);
  });

  it('nombra el mes', () => {
    expect(mesLargo('2026-03', ['enero', 'febrero', 'marzo'])).toBe('marzo 2026');
  });
});
