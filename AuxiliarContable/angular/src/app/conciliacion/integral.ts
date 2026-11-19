// La conciliación integral (botón «Conciliar»): tipos y cómo se agrupan sus pasos, sin dependencias de Angular.

export interface PasoDeConciliacionDto {
  /** AAAA-MM; vacío si el paso es de toda la vuelta. */
  periodo: string;
  clave: string;
  titulo: string;
  estado: 'pendiente' | 'trabajando' | 'hecho' | 'aviso' | 'error' | 'omitido';
  resumen?: string | null;
}

export interface AlertaNormativaDto {
  disparador: string;
  pregunta: string;
  fuentes: string[];
}

export interface MesConciliadoDto {
  anio: number;
  mes: number;
  polizas: number;
  detenidas: number;
  isrAPagar?: number | null;
  ivaAPagar?: number | null;
  ivaAFavor?: number | null;
  utilidadDelEjercicio?: number | null;
  estadosPreliminares: boolean;
  notas: number;
  avisos: string[];
  alertasNormativas: AlertaNormativaDto[];
}

export interface ConciliacionIntegralDto {
  id: string;
  anio: number;
  mes: number;
  estado: 'Pendiente' | 'Trabajando' | 'Lista' | 'Error';
  pedidaUtc: string;
  iniciadaUtc?: string | null;
  terminadaUtc?: string | null;
  pasos: PasoDeConciliacionDto[];
  meses: MesConciliadoDto[];
  error?: string | null;
}

export interface GrupoDePasos {
  /** «Toda la vuelta» o «2026-03». */
  periodo: string;
  pasos: PasoDeConciliacionDto[];
  hechos: number;
}

export function enCurso(c: ConciliacionIntegralDto | null | undefined): boolean {
  return !!c && (c.estado === 'Pendiente' || c.estado === 'Trabajando');
}

/** Los pasos por mes, en el orden en que corren. */
export function agrupar(pasos: PasoDeConciliacionDto[]): GrupoDePasos[] {
  const grupos: GrupoDePasos[] = [];
  for (const p of pasos) {
    const periodo = p.periodo || '';
    let g = grupos.find(x => x.periodo === periodo);
    if (!g) {
      g = { periodo, pasos: [], hechos: 0 };
      grupos.push(g);
    }
    g.pasos.push(p);
    if (p.estado !== 'pendiente' && p.estado !== 'trabajando') {
      g.hechos++;
    }
  }
  return grupos;
}

/** Cuántos pasos van terminados de todos (para la barra). */
export function avance(c: ConciliacionIntegralDto | null | undefined): number {
  if (!c || !c.pasos.length) {
    return 0;
  }
  const hechos = c.pasos.filter(p => p.estado !== 'pendiente' && p.estado !== 'trabajando').length;
  return Math.round((100 * hechos) / c.pasos.length);
}

export const ICONO_DE_PASO: Record<PasoDeConciliacionDto['estado'], string> = {
  pendiente: 'bi-circle',
  trabajando: 'bi-arrow-repeat',
  hecho: 'bi-check-circle-fill',
  aviso: 'bi-exclamation-circle-fill',
  error: 'bi-x-circle-fill',
  omitido: 'bi-dash-circle',
};
