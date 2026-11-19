/** Fechas de los periodos de nómina, en AAAA-MM-DD (sin zona horaria: son días). */
export type Periodicidad = 'semanal' | 'catorcenal' | 'quincenal' | 'mensual';

function iso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** El fin de un periodo que empieza en `inicio`: semana 7 días, catorcena 14, quincena hasta el 15 o fin de mes, mes completo. */
export function finDelPeriodo(inicio: string, periodicidad: Periodicidad): string {
  const [a, m, d] = inicio.split('-').map(Number);
  const ini = new Date(a, m - 1, d);
  switch (periodicidad) {
    case 'semanal':
      return iso(new Date(a, m - 1, d + 6));
    case 'catorcenal':
      return iso(new Date(a, m - 1, d + 13));
    case 'quincenal':
      return d <= 15 ? iso(new Date(a, m - 1, 15)) : iso(new Date(a, m, 0));
    default:
      return iso(new Date(ini.getFullYear(), ini.getMonth() + 1, 0));
  }
}

/** El siguiente periodo después de uno que terminó en `fin`. */
export function siguienteInicio(fin: string): string {
  const [a, m, d] = fin.split('-').map(Number);
  return iso(new Date(a, m - 1, d + 1));
}

/** Para un `<input type="date">`: la fecha del servidor (con hora) queda en AAAA-MM-DD; vacío o null queda null. */
export function soloDia(v: string | null | undefined): string | null {
  return v ? v.slice(0, 10) : null;
}
