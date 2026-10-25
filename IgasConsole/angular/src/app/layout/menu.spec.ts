import { describe, expect, it } from 'vitest';
import { casaConRuta, clavesQueContienen, construirMenu, idDeGrupo, leerAbiertos } from './menu';

type N = { name: string; path?: string; iconClass?: string; children?: N[] };
const n = (name: string, path?: string, children?: N[]): N => ({ name, path, iconClass: 'bi bi-x', children });
const menu = (arbol: N[]) => construirMenu(arbol as any, c => c.replace('::', ''));

const ARBOL: N[] = [
  n('::Menu:Home', '/'),
  n('::Menu:MiCuenta', undefined, [n('::Cuenta:Resumen', '/mi-cuenta')]),
  n('::Menu:Estaciones', undefined, [
    n('::E:Resumen', '/estaciones/resumen'),
    n('::E:Grupo:Operacion', undefined, [n('::E:Pista', '/estaciones/pista'), n('::E:Tanques', '/estaciones/tanques')]),
    // Sin hijos visibles (ABP ya quitó los que no tienen permiso): no se pinta.
    n('::E:Grupo:Ventas', undefined, []),
    n('::E:Grupo:Tecnico', undefined, [n('::E:Nodo', '/estaciones/nodo')]),
  ]),
  n('AbpUiNavigation::Menu:Administration', undefined, [
    n('AbpIdentity::Menu:IdentityManagement', undefined, [n('Users', '/identity/users'), n('Roles', '/identity/roles')]),
  ]),
  n('::Menu:Vacia', undefined, [n('::Menu:SubVacia', undefined, [])]),
];

describe('construirMenu', () => {
  const { secciones, cuenta } = menu(ARBOL);

  it('Mi cuenta va al menú de la sesión; lo suelto, en una sección sin título', () => {
    expect(cuenta.map(i => i.ruta)).toEqual(['/mi-cuenta']);
    expect(secciones[0].titulo).toBeUndefined();
    expect(secciones[0].items.map(i => i.ruta)).toEqual(['/']);
  });

  it('Estaciones: Resumen suelto y subgrupos en orden; el vacío no sale', () => {
    const est = secciones.find(s => s.clave === '::Menu:Estaciones')!;
    expect(est.entradas.map(e => (e.tipo === 'item' ? e.item.ruta : e.grupo.clave))).toEqual([
      '/estaciones/resumen',
      '::E:Grupo:Operacion',
      '::E:Grupo:Tecnico',
    ]);
    expect(est.items.map(i => i.ruta)).toEqual(['/estaciones/resumen', '/estaciones/pista', '/estaciones/tanques', '/estaciones/nodo']);
  });

  it('una sección con un único subgrupo se aplana (Administración › Usuarios)', () => {
    const admin = secciones.find(s => s.clave === 'AbpUiNavigation::Menu:Administration')!;
    expect(admin.entradas.every(e => e.tipo === 'item')).toBe(true);
    expect(admin.items.map(i => i.ruta)).toEqual(['/identity/users', '/identity/roles']);
  });

  it('una sección sin ningún enlace visible no sale', () => {
    expect(secciones.some(s => s.clave === '::Menu:Vacia')).toBe(false);
  });
});

describe('clavesQueContienen', () => {
  const { secciones } = menu(ARBOL);

  it('abre la sección y el subgrupo de la pantalla actual (también subrutas y con query)', () => {
    expect(clavesQueContienen(secciones, '/estaciones/tanques?org=1')).toEqual(['::Menu:Estaciones', '::E:Grupo:Operacion']);
    expect(clavesQueContienen(secciones, '/estaciones/nodo/abc')).toEqual(['::Menu:Estaciones', '::E:Grupo:Tecnico']);
  });

  it('un enlace suelto de la sección abre solo la sección; la portada, nada', () => {
    expect(clavesQueContienen(secciones, '/estaciones/resumen')).toEqual(['::Menu:Estaciones']);
    expect(clavesQueContienen(secciones, '/')).toEqual([]);
    expect(clavesQueContienen(secciones, '/no-existe')).toEqual([]);
  });
});

describe('utilidades', () => {
  it('casaConRuta: / solo consigo misma; prefijo con barra', () => {
    expect(casaConRuta('/', '/')).toBe(true);
    expect(casaConRuta('/', '/estaciones')).toBe(false);
    expect(casaConRuta('/estaciones/pista', '/estaciones/pista#x')).toBe(true);
    expect(casaConRuta('/estaciones/pista', '/estaciones/pistas')).toBe(false);
  });

  it('idDeGrupo da un id válido y estable', () => {
    expect(idDeGrupo('::Menu:Estaciones:Grupo:Operacion')).toBe('nav-menu-estaciones-grupo-operacion');
  });

  it('leerAbiertos tolera basura', () => {
    expect([...leerAbiertos('["a","b",3]')]).toEqual(['a', 'b']);
    expect(leerAbiertos('{roto').size).toBe(0);
    expect(leerAbiertos(null).size).toBe(0);
    expect(leerAbiertos('{"a":1}').size).toBe(0);
  });
});
