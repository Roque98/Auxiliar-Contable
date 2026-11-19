import { describe, expect, it } from 'vitest';
import { finDelPeriodo, siguienteInicio } from './fechas';

describe('periodos de nómina', () => {
  it('quincenas: 1 al 15 y 16 al fin de mes (febrero incluido)', () => {
    expect(finDelPeriodo('2026-03-01', 'quincenal')).toBe('2026-03-15');
    expect(finDelPeriodo('2026-03-16', 'quincenal')).toBe('2026-03-31');
    expect(finDelPeriodo('2026-02-16', 'quincenal')).toBe('2026-02-28');
  });

  it('semana, catorcena y mes', () => {
    expect(finDelPeriodo('2026-12-28', 'semanal')).toBe('2027-01-03');
    expect(finDelPeriodo('2026-03-02', 'catorcenal')).toBe('2026-03-15');
    expect(finDelPeriodo('2026-04-01', 'mensual')).toBe('2026-04-30');
  });

  it('el siguiente empieza al otro día', () => {
    expect(siguienteInicio('2026-03-31')).toBe('2026-04-01');
  });
});
