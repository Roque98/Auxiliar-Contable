import { describe, expect, it } from 'vitest';
import {
  AlmacenPlegable,
  PREFIJO_PLEGABLE,
  abiertaAlEmpezar,
  guardarPlegable,
  hayAtencion,
  leerPlegable,
  siguienteCabecera,
} from './plegable';

function memoria(): AlmacenPlegable & { datos: Map<string, string> } {
  const datos = new Map<string, string>();
  return { datos, getItem: k => datos.get(k) ?? null, setItem: (k, v) => void datos.set(k, v) };
}

describe('leerPlegable / guardarPlegable', () => {
  it('sin nada guardado devuelve null', () => {
    expect(leerPlegable(memoria(), 'vol.bitacora')).toBeNull();
  });

  it('recuerda abierta y cerrada con su prefijo', () => {
    const m = memoria();
    guardarPlegable(m, 'vol.bitacora', false);
    expect(m.datos.get(PREFIJO_PLEGABLE + 'vol.bitacora')).toBe('0');
    expect(leerPlegable(m, 'vol.bitacora')).toBe(false);
    guardarPlegable(m, 'vol.bitacora', true);
    expect(leerPlegable(m, 'vol.bitacora')).toBe(true);
  });

  it('un valor raro cuenta como nunca tocada', () => {
    const m = memoria();
    m.datos.set(PREFIJO_PLEGABLE + 'x', 'quizá');
    expect(leerPlegable(m, 'x')).toBeNull();
  });

  it('sin almacén, o con uno que revienta, no falla', () => {
    const roto: AlmacenPlegable = {
      getItem: () => {
        throw new Error('SecurityError');
      },
      setItem: () => {
        throw new Error('QuotaExceeded');
      },
    };
    expect(leerPlegable(null, 'x')).toBeNull();
    expect(leerPlegable(roto, 'x')).toBeNull();
    expect(() => guardarPlegable(roto, 'x', true)).not.toThrow();
    expect(() => guardarPlegable(null, 'x', true)).not.toThrow();
  });
});

describe('abiertaAlEmpezar', () => {
  it('sin nada guardado, lo que diga la pantalla', () => {
    expect(abiertaAlEmpezar({ porDefecto: true, guardado: null, alerta: false })).toBe(true);
    expect(abiertaAlEmpezar({ porDefecto: false, guardado: null, alerta: false })).toBe(false);
  });

  it('lo elegido por el usuario gana al valor por defecto', () => {
    expect(abiertaAlEmpezar({ porDefecto: true, guardado: false, alerta: false })).toBe(false);
    expect(abiertaAlEmpezar({ porDefecto: false, guardado: true, alerta: false })).toBe(true);
  });

  it('con errores o avisos se abre aunque el usuario la cerrara', () => {
    expect(abiertaAlEmpezar({ porDefecto: false, guardado: false, alerta: true })).toBe(true);
  });
});

describe('hayAtencion', () => {
  it('error y alerta piden atención; éxito y neutro no', () => {
    expect(hayAtencion(['exito', 'neutro'])).toBe(false);
    expect(hayAtencion([])).toBe(false);
    expect(hayAtencion(['exito', 'alerta'])).toBe(true);
    expect(hayAtencion(['error'])).toBe(true);
  });
});

describe('siguienteCabecera', () => {
  it('flechas con vuelta, Inicio y Fin', () => {
    expect(siguienteCabecera(0, 3, 'ArrowDown')).toBe(1);
    expect(siguienteCabecera(2, 3, 'ArrowDown')).toBe(0);
    expect(siguienteCabecera(0, 3, 'ArrowUp')).toBe(2);
    expect(siguienteCabecera(1, 3, 'Home')).toBe(0);
    expect(siguienteCabecera(1, 3, 'End')).toBe(2);
  });

  it('otras teclas o índices imposibles no mueven el foco', () => {
    expect(siguienteCabecera(0, 3, 'Enter')).toBeNull();
    expect(siguienteCabecera(0, 3, ' ')).toBeNull();
    expect(siguienteCabecera(0, 0, 'ArrowDown')).toBeNull();
    expect(siguienteCabecera(-1, 3, 'ArrowDown')).toBeNull();
  });
});
