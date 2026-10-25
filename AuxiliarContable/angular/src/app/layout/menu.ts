import type { ABP, TreeNode } from '@abp/ng.core';

/*
 * El MENÚ LATERAL como datos: del árbol de rutas de ABP (ya filtrado por permisos en
 * `visible$`) a secciones, subgrupos y enlaces. Sin Angular ni DOM, para probarlo en node
 * (vitest.config.mts); el layout solo lo pinta.
 */

/** Un enlace del menú, ya traducido. */
export interface ItemMenu {
  nombre: string;
  ruta: string;
  icono: string;
}

/** Un subgrupo plegable dentro de una sección (p. ej. Estaciones › Operación). */
export interface SubgrupoMenu {
  clave: string;
  titulo: string;
  icono: string;
  items: ItemMenu[];
}

export type EntradaMenu = { tipo: 'item'; item: ItemMenu } | { tipo: 'grupo'; grupo: SubgrupoMenu };

/** Un bloque del menú: los sueltos van sin título (y sin plegar); los grupos, con el suyo. */
export interface SeccionMenu {
  clave?: string;
  titulo?: string;
  /** En orden: enlaces y subgrupos mezclados, como los ordena ABP. */
  entradas: EntradaMenu[];
  /** Todos los enlaces de la sección, aplanados (título de la página, búsqueda de la ruta). */
  items: ItemMenu[];
}

export const GRUPO_CUENTA = '::Menu:MiCuenta';
const ICONO_POR_DEFECTO = 'bi bi-dot';

/**
 * Convierte el árbol en secciones. «Mi cuenta» no va al menú lateral: va al menú de la
 * sesión. Un nivel de subgrupo como mucho; lo más hondo se aplana dentro de él.
 *
 * Una sección cuyo único contenido es UN subgrupo se aplana (Administración › Identidad ›
 * Usuarios queda como Administración › Usuarios): un clic más para nada. Un subgrupo sin
 * enlaces visibles desaparece, y una sección sin nada, también.
 */
export function construirMenu(
  arbol: TreeNode<ABP.Route>[],
  traducir: (clave: string) => string,
): { secciones: SeccionMenu[]; cuenta: ItemMenu[] } {
  const sueltos: ItemMenu[] = [];
  const grupos: SeccionMenu[] = [];
  let cuenta: ItemMenu[] = [];

  const hoja = (nodo: TreeNode<ABP.Route>): ItemMenu => ({
    nombre: traducir(nodo.name),
    ruta: nodo.path!,
    icono: nodo.iconClass || ICONO_POR_DEFECTO,
  });
  const hojas = (nodo: TreeNode<ABP.Route>): ItemMenu[] =>
    nodo.children?.length ? nodo.children.flatMap(hojas) : nodo.path ? [hoja(nodo)] : [];

  for (const nodo of arbol) {
    if (nodo.name === GRUPO_CUENTA) {
      cuenta = hojas(nodo);
      continue;
    }
    if (!nodo.children?.length) {
      sueltos.push(...hojas(nodo));
      continue;
    }
    let entradas: EntradaMenu[] = [];
    for (const hijo of nodo.children) {
      if (hijo.children?.length) {
        const items = hojas(hijo);
        if (items.length) {
          entradas.push({
            tipo: 'grupo',
            grupo: { clave: hijo.name, titulo: traducir(hijo.name), icono: hijo.iconClass || ICONO_POR_DEFECTO, items },
          });
        }
      } else if (hijo.path) {
        entradas.push({ tipo: 'item', item: hoja(hijo) });
      }
    }
    if (entradas.length === 1 && entradas[0].tipo === 'grupo') {
      entradas = entradas[0].grupo.items.map(item => ({ tipo: 'item' as const, item }));
    }
    const items = entradas.flatMap(e => (e.tipo === 'item' ? [e.item] : e.grupo.items));
    if (items.length) {
      grupos.push({ clave: nodo.name, titulo: traducir(nodo.name), entradas, items });
    }
  }

  const primera: SeccionMenu[] = sueltos.length
    ? [{ entradas: sueltos.map(item => ({ tipo: 'item' as const, item })), items: sueltos }]
    : [];
  return { secciones: [...primera, ...grupos], cuenta };
}

/** ¿Este enlace es (o contiene) la URL? `/` solo casa consigo misma. */
export function casaConRuta(ruta: string, url: string): boolean {
  const limpia = url.split(/[?#]/)[0];
  return ruta === '/' ? limpia === '/' : limpia === ruta || limpia.startsWith(ruta + '/');
}

/**
 * Las claves (sección y subgrupo) que hay que abrir para que se vea el enlace de la URL
 * actual: el de ruta más larga que case, como el título de la página.
 */
export function clavesQueContienen(secciones: SeccionMenu[], url: string): string[] {
  let mejor: { largo: number; claves: string[] } | null = null;
  for (const s of secciones) {
    for (const e of s.entradas) {
      const candidatos = e.tipo === 'item' ? [e.item] : e.grupo.items;
      for (const i of candidatos) {
        if (casaConRuta(i.ruta, url) && (!mejor || i.ruta.length > mejor.largo)) {
          const claves = [s.clave, e.tipo === 'grupo' ? e.grupo.clave : undefined].filter((c): c is string => !!c);
          mejor = { largo: i.ruta.length, claves };
        }
      }
    }
  }
  return mejor?.claves ?? [];
}

/** El id del DOM para `aria-controls` a partir de una clave de ruta (`::Menu:X` → `nav-menu-x`). */
export function idDeGrupo(clave: string): string {
  return 'nav-' + clave.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '').toLowerCase();
}

/** Lo guardado de qué grupos dejó abiertos el usuario (JSON con un array de claves). */
export function leerAbiertos(texto: string | null): Set<string> {
  try {
    const v = JSON.parse(texto ?? '[]');
    return new Set(Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : []);
  } catch {
    return new Set();
  }
}
