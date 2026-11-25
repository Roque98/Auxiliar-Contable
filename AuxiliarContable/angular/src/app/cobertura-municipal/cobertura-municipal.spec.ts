import { describe, expect, it } from 'vitest';
import {
  BalanceDto, COLUMNAS_MOSAICO, FILAS_MOSAICO, MOSAICO, celdasDelMosaico, csvDeCobertura, nivelEstatal, nivelMunicipal, normasPorClase,
  textoDePrioridad, tramosDeBalance,
} from './cobertura-municipal';

const balance = (b: Partial<BalanceDto>): BalanceDto => ({
  universo: 40, tenemos: 0, noAplica: 0, noAplicaPorOtraAutoridad: 0, noAplicaPorNoExistir: 0, noEncontrado: 0, sinRevisar: 0,
  avancePct: 0, completo: false, ...b,
});

describe('csvDeCobertura', () => {
  it('lista cada municipio con su estado y cobertura, con BOM para Excel y comillas escapadas', () => {
    const csv = csvDeCobertura({
      cubiertos: 1, parciales: 0, sinFuente: 0, enProceso: 1, noCubiertos: 1, clientes: [],
      estados: [{ estado: 'JAL', nombre: 'Jalisco', municipios: 3, cubiertos: ['Guadalajara'], parciales: ['Tonalá'], sinFuente: [], enProceso: ['Zapopan'], noCubiertos: ['Villa "Corona"'] }],
    });
    expect(csv.startsWith('﻿')).toBe(true);
    const filas = csv.slice(1).split('\r\n');
    expect(filas[0]).toBe('"Estado","Municipio","Cobertura"');
    expect(filas).toContain('"Jalisco","Guadalajara","Cubierto"');
    expect(filas).toContain('"Jalisco","Zapopan","En proceso"');
    expect(filas).toContain('"Jalisco","Tonalá","Parcial"');
    expect(filas).toContain('"Jalisco","Villa ""Corona""","No cubierto"');
  });
});

describe('mosaico de México', () => {
  it('pone los 32 estados en lugares distintos dentro de la rejilla', () => {
    const lugares = Object.values(MOSAICO).map(([c, f]) => `${c},${f}`);
    expect(lugares).toHaveLength(32);
    expect(new Set(lugares).size).toBe(32);
    expect(Object.values(MOSAICO).every(([c, f]) => c < COLUMNAS_MOSAICO && f < FILAS_MOSAICO)).toBe(true);
  });

  it('las celdas van en orden de lectura con huecos vacíos; un estado desconocido no se pinta', () => {
    const celdas = celdasDelMosaico([{ estado: 'BC' }, { estado: 'CHIS' }, { estado: 'XX' }]);
    expect(celdas).toHaveLength(COLUMNAS_MOSAICO * FILAS_MOSAICO);
    expect(celdas[0]).toEqual({ estado: 'BC' });
    expect(celdas[5 * COLUMNAS_MOSAICO + 8]).toEqual({ estado: 'CHIS' });
    expect(celdas.filter(c => c !== null)).toHaveLength(2);
  });
});

describe('niveles del mapa', () => {
  it('municipal por % de población cubierta', () => {
    expect([null, 10, 15, 40, 60, 96].map(nivelMunicipal)).toEqual([0, 0, 1, 2, 3, 4]);
  });

  it('estatal: completo es 4 y baja según lo que falta del catálogo', () => {
    expect(nivelEstatal(balance({ universo: 72, completo: true }))).toBe(4);
    expect(nivelEstatal(balance({ universo: 72, noEncontrado: 2 }))).toBe(3);
    expect(nivelEstatal(balance({ universo: 72, noEncontrado: 42 }))).toBe(0);
    expect(nivelEstatal(undefined)).toBe(0);
  });
});

describe('balance de una ficha', () => {
  it('la barra suma el universo', () => {
    const t = tramosDeBalance(balance({ tenemos: 10, noAplica: 20, noEncontrado: 2, sinRevisar: 8 }));
    expect(t.map(x => x.clase)).toEqual(['tenemos', 'no_aplica', 'no_encontrado', 'sin_revisar']);
    expect(t.reduce((a, x) => a + x.pct, 0)).toBeCloseTo(100, 5);
  });

  it('agrupa las normas por clase en orden y omite las clases vacías', () => {
    const g = normasPorClase([
      { id: 'b', clase: 'sin_revisar' }, { id: 'a', clase: 'tenemos' }, { id: 'c', clase: 'no_aplica', motivo: 'A) Lo regula el estado' },
    ]);
    expect(g.map(x => x.clase)).toEqual(['tenemos', 'no_aplica', 'sin_revisar']);
    expect(g[1].normas[0].motivo).toContain('A)');
  });
});

describe('textoDePrioridad', () => {
  it('escribe una carpeta por línea y descarta lo que no es «EDO/municipio»', () => {
    const t = textoDePrioridad({ municipios: ['JAL/zapopan', 'MOR/cuernavaca', '../etc', 'JAL/Zapopan Centro'], sinMunicipio: 0 }, '2026-11-25');
    const lineas = t.split('\n').filter(l => l && !l.startsWith('#'));
    expect(lineas).toEqual(['JAL/zapopan', 'MOR/cuernavaca']);
    expect(t).toContain('2026-11-25');
  });
});
