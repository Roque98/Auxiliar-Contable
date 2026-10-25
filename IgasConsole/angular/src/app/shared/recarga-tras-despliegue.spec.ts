import { describe, expect, it } from 'vitest';
import { EsFalloDeCarga } from './recarga-tras-despliegue';

/**
 * Los mensajes reales de los dos constructores. La razón de que esto tenga test: el nombre
 * `ChunkLoadError` es de webpack, y este proyecto usa el `application builder`, que lanza un
 * `TypeError` con otro texto. Reconociendo solo el primero, el arreglo no haría nada — y no
 * fallaría nada, que es justo lo que hay que evitar.
 */
describe('EsFalloDeCarga', () => {
  it('reconoce el ChunkLoadError de webpack', () => {
    const e = new Error('Loading chunk 42 failed.');
    e.name = 'ChunkLoadError';
    expect(EsFalloDeCarga(e)).toBe(true);
  });

  it('reconoce el import() nativo del application builder de Angular', () => {
    expect(
      EsFalloDeCarga(
        new TypeError('Failed to fetch dynamically imported module: https://x/chunk-AB12.js'),
      ),
    ).toBe(true);
  });

  it('reconoce la variante de Firefox y la de Safari', () => {
    expect(EsFalloDeCarga(new TypeError('error loading dynamically imported module'))).toBe(true);
    expect(EsFalloDeCarga(new TypeError('Importing a module script failed.'))).toBe(true);
  });

  it('NO se lleva por delante los errores normales de la aplicación', () => {
    expect(EsFalloDeCarga(new Error('Cannot read properties of undefined'))).toBe(false);
    expect(EsFalloDeCarga(new Error('Http failure response: 500 Internal Server Error'))).toBe(false);
    expect(EsFalloDeCarga(null)).toBe(false);
    expect(EsFalloDeCarga(undefined)).toBe(false);
  });
});
