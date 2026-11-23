import { describe, expect, it } from 'vitest';
import { csvDeCobertura } from './cobertura-municipal';

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
