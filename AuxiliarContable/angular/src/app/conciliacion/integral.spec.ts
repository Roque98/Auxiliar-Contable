import { describe, expect, it } from 'vitest';
import { ConciliacionIntegralDto, agrupar, avance, enCurso } from './integral';

const vuelta = (estados: string[]): ConciliacionIntegralDto => ({
  id: '1', anio: 2026, mes: 2, estado: 'Trabajando', pedidaUtc: '', meses: [],
  pasos: estados.map((e, i) => ({
    periodo: i === 0 ? '' : i < 3 ? '2026-01' : '2026-02', clave: `p${i}`, titulo: `Paso ${i}`,
    estado: e as ConciliacionIntegralDto['pasos'][number]['estado'],
  })),
});

describe('conciliación integral', () => {
  it('agrupa los pasos por mes en su orden', () => {
    const g = agrupar(vuelta(['hecho', 'hecho', 'aviso', 'trabajando', 'pendiente']).pasos);
    expect(g.map(x => x.periodo)).toEqual(['', '2026-01', '2026-02']);
    expect(g.map(x => x.hechos)).toEqual([1, 2, 0]);
  });

  it('el avance cuenta lo terminado, sea bien, aviso, error u omitido', () => {
    expect(avance(vuelta(['hecho', 'aviso', 'error', 'omitido', 'pendiente']))).toBe(80);
    expect(avance(null)).toBe(0);
  });

  it('sabe si sigue en curso', () => {
    expect(enCurso(vuelta([]))).toBe(true);
    expect(enCurso({ ...vuelta([]), estado: 'Lista' })).toBe(false);
  });
});
