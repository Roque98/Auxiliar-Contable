/**
 * El papel de una persona DENTRO de una organización (espejo de RolEnOrganizacion en el
 * servidor). No es un rol global: solo vale en esa organización.
 */
export enum RolEnOrganizacion {
  Lector = 0,
  Miembro = 1,
  Administrador = 2,
}

/** De más a menos, como se ofrecen en las listas. */
export const PAPELES: { valor: RolEnOrganizacion; texto: string; detalle: string }[] = [
  {
    valor: RolEnOrganizacion.Administrador,
    texto: 'Administrador',
    detalle: 'Sube, ve y gestiona a la gente de su organización (invitar, quitar, cambiar su papel).',
  },
  { valor: RolEnOrganizacion.Miembro, texto: 'Miembro', detalle: 'Sube sus reportes y ve los de su organización.' },
  { valor: RolEnOrganizacion.Lector, texto: 'Solo ve', detalle: 'Ve los análisis de su organización, sin subir.' },
];

export function textoDePapel(r: RolEnOrganizacion | null | undefined): string {
  return PAPELES.find(p => p.valor === r)?.texto ?? 'Miembro';
}
