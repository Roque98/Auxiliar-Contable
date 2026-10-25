/**
 * Los roles de la jerarquía, como los ve una persona. Es el espejo de `IgasRoles` en el
 * backend (Domain.Shared/Identity/IgasRoles.cs): nombres y niveles tienen que coincidir.
 */
export const ROLES_PROPIOS: Record<string, { etiqueta: string; nivel: number; icono: string }> = {
  admin: { etiqueta: 'Developer', nivel: 3, icono: 'bi-code-slash' },
  owner: { etiqueta: 'Owner', nivel: 2, icono: 'bi-star' },
  administrador: { etiqueta: 'Administrador', nivel: 1, icono: 'bi-shield-check' },
  usuario: { etiqueta: 'Usuario', nivel: 0, icono: 'bi-person' },
};

/** «owner» → «Owner»; un rol creado a mano se enseña tal cual. */
export function etiquetaRol(nombre: string | null | undefined): string {
  if (!nombre) {
    return '';
  }
  return ROLES_PROPIOS[nombre.toLowerCase()]?.etiqueta ?? nombre;
}

/** Nivel del rol (0 para cualquiera que no sea de la jerarquía). */
export function nivelRol(nombre: string | null | undefined): number {
  return nombre ? (ROLES_PROPIOS[nombre.toLowerCase()]?.nivel ?? 0) : 0;
}

/** Qué hace cada rol, en una línea, para elegir sin tener que saberse los permisos. */
export function describirRol(nombre: string): { icono: string; texto: string } {
  switch (nombre.toLowerCase()) {
    case 'admin':
      return { icono: 'bi-code-slash', texto: 'Todo, incluidas las herramientas técnicas. Nadie más lo ve.' };
    case 'owner':
      return { icono: 'bi-star', texto: 'Dueño del negocio: gestiona administradores y usuarios, y ve todo.' };
    case 'administrador':
      return { icono: 'bi-shield-check', texto: 'Del equipo del despacho: ve a todos los clientes, los organiza e invita.' };
    case 'usuario':
      return { icono: 'bi-person', texto: 'Alguien del cliente: ve y sube la información de su organización.' };
    default:
      return { icono: 'bi-person-badge', texto: 'Rol personalizado: los permisos que le haya dado el Owner.' };
  }
}

/** Los roles ordenados como se leen: el más alto primero. */
export function ordenarRoles<T>(roles: T[], nombre: (r: T) => string | undefined): T[] {
  return [...roles].sort((a, b) => nivelRol(nombre(b)) - nivelRol(nombre(a)) || (nombre(a) ?? '').localeCompare(nombre(b) ?? ''));
}
