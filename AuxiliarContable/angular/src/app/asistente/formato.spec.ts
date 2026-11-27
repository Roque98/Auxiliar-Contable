import { describe, expect, it } from 'vitest';
import { bloquesDeRespuesta, trozos } from './formato';

describe('bloquesDeRespuesta (la respuesta del asistente ordenada)', () => {
  it('arma la tabla del cálculo y marca las columnas de cifras', () => {
    const b = bloquesDeRespuesta('Cálculo:\n\n| Concepto | Importe |\n|---|---:|\n| Base | 2,241.47 |\n| **ISR a retener** | **37.76** |');
    expect(b[0].tipo).toBe('parrafo');
    const t = b[1];
    expect(t.tipo).toBe('tabla');
    expect(t.filas?.length).toBe(2);
    expect(t.numericas).toEqual([false, true]);
    expect(t.filas?.[1][1]).toEqual([{ texto: '37.76', negrita: true }]);
  });

  it('reconoce el fundamento como título final', () => {
    const b = bloquesDeRespuesta('Respuesta.\n\n**Fundamento**\n- Art. 9 LISR');
    expect(b[1]).toEqual({ tipo: 'titulo', trozos: [{ texto: 'Fundamento', negrita: false }], fundamento: true });
    expect(b[2].tipo).toBe('lista');
  });

  it('listas numeradas y con viñetas por separado', () => {
    const b = bloquesDeRespuesta('1. Uno\n2. Dos\n- a\n- b');
    expect(b.map(x => x.tipo)).toEqual(['lista', 'lista']);
  });

  it('nunca produce HTML: las etiquetas quedan como texto', () => {
    const b = bloquesDeRespuesta('<script>alert(1)</script>');
    expect(b).toEqual([{ tipo: 'parrafo', trozos: [{ texto: '<script>alert(1)</script>', negrita: false }] }]);
  });

  it('un ** sin cerrar se queda como texto', () => {
    expect(trozos('a **b')).toEqual([{ texto: 'a ', negrita: false }, { texto: '**b', negrita: false }]);
  });
});
