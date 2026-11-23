/** Tipos y funciones puras del reporte de cobertura municipal (sin Angular: se prueban con vitest). */

export interface ClienteSinCoberturaDto {
  organizacionId: string;
  nombre: string;
  estado?: string | null;
  municipio?: string | null;
  cobertura: 'parcial' | 'sin_fuente' | 'seleccionado' | 'no_cubierto' | 'sin_municipio';
}

export interface CoberturaDeEstadoDto {
  estado: string;
  nombre: string;
  municipios: number;
  poblacionCubiertaPct?: number | null;
  poblacionSeleccionadaPct?: number | null;
  cubiertos: string[];
  parciales: string[];
  sinFuente: string[];
  enProceso: string[];
  noCubiertos: string[];
}

export interface CoberturaMunicipalDto {
  criterio?: string | null;
  cubiertos: number;
  parciales: number;
  sinFuente: number;
  minimoCubierto?: number;
  enProceso: number;
  noCubiertos: number;
  clientes: ClienteSinCoberturaDto[];
  estados: CoberturaDeEstadoDto[];
  aviso?: string | null;
}

/** El reporte en CSV (estado, municipio, cobertura) para abrirlo en Excel. */
export function csvDeCobertura(r: CoberturaMunicipalDto): string {
  const filas = [['Estado', 'Municipio', 'Cobertura']];
  for (const e of r.estados) {
    for (const [lista, texto] of [[e.cubiertos, 'Cubierto'], [e.parciales ?? [], 'Parcial'], [e.sinFuente ?? [], 'Sin fuente en línea'],
      [e.enProceso, 'En proceso'], [e.noCubiertos, 'No cubierto']] as const) {
      for (const m of lista) {
        filas.push([e.nombre, m, texto]);
      }
    }
  }
  return '﻿' + filas.map(f => f.map(c => `"${c.replace(/"/g, '""')}"`).join(',')).join('\r\n');
}
