import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Tokens del design system mal usados. El navegador DESCARTA en silencio una declaración
 * CSS inválida: no hay error, simplemente el borde o el fondo no se pinta.
 *
 * Pasó con `--ig-borde`, que ya es un borde COMPLETO (`1px solid …`): escrito como
 * `1px solid var(--ig-borde)` o como fondo, los divisores de Actividad, Organizaciones y
 * el auditor no se dibujaban, y la pista de las barras de comparación tampoco.
 */
const raiz = join(__dirname, '..');

function hojas(dir: string): string[] {
  return readdirSync(dir).flatMap(nombre => {
    const ruta = join(dir, nombre);
    if (statSync(ruta).isDirectory()) {
      return hojas(ruta);
    }
    return /\.(scss|css)$/.test(nombre) || /\.component\.ts$/.test(nombre) ? [ruta] : [];
  });
}

describe('tokens del design system', () => {
  it('--ig-borde solo se usa como borde completo', () => {
    // Válido: `border: var(--ig-borde)`, `border-bottom: var(--ig-borde)`…
    const valido = /^\s*border(-top|-right|-bottom|-left)?\s*:\s*var\(--ig-borde\)\s*;?\s*$/;
    const mal: string[] = [];
    for (const archivo of hojas(raiz)) {
      readFileSync(archivo, 'utf8')
        .split('\n')
        .forEach((linea, i) => {
          if (linea.includes('var(--ig-borde)') && !linea.includes('--ig-borde:') && !valido.test(linea)) {
            mal.push(`${relative(raiz, archivo)}:${i + 1}  ${linea.trim()}`);
          }
        });
    }
    expect(mal, mal.join('\n')).toEqual([]);
  });
});
