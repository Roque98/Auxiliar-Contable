import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

const raiz = __dirname;
const css = readFileSync(join(raiz, '../../node_modules/bootstrap-icons/font/bootstrap-icons.css'), 'utf8');

function archivos(dir: string): string[] {
  return readdirSync(dir).flatMap(nombre => {
    const ruta = join(dir, nombre);
    if (statSync(ruta).isDirectory()) return archivos(ruta);
    return /\.(ts|html)$/.test(nombre) && !nombre.endsWith('.spec.ts') ? [ruta] : [];
  });
}

describe('iconos', () => {
  // Una clase de icono que no existe no da error: deja un hueco vacío y la pantalla parece a
  // medio hacer. Pasó con bi-person-vcard (perfil fiscal) y bi-envelope-paper: no vienen en la
  // versión de bootstrap-icons del proyecto.
  it('todo bi-* usado existe en bootstrap-icons', () => {
    const faltan: string[] = [];
    for (const archivo of archivos(raiz)) {
      for (const icono of new Set(readFileSync(archivo, 'utf8').match(/\bbi-[a-z0-9]+(?:-[a-z0-9]+)*/g) ?? [])) {
        if (!css.includes(`.${icono}::before`)) faltan.push(`${relative(raiz, archivo)}: ${icono}`);
      }
    }
    expect(faltan, faltan.join('\n')).toEqual([]);
  });
});
