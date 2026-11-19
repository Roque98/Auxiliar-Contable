import { describe, expect, it } from 'vitest';
import { leerResultado } from './resultado';

describe('leerResultado', () => {
  it('lee el JSON del motor', () => {
    const r = leerResultado(JSON.stringify({ analisis: { conclusion: 'Tasa 0%', confianza: 'media' }, expediente: { ejercicio: 2026 } }));
    expect(r?.analisis.conclusion).toBe('Tasa 0%');
    expect(r?.expediente.ejercicio).toBe(2026);
  });

  it('devuelve null si no hay análisis o el JSON está roto', () => {
    expect(leerResultado(null)).toBeNull();
    expect(leerResultado('{"expediente":{}}')).toBeNull();
    expect(leerResultado('{roto')).toBeNull();
  });
});
