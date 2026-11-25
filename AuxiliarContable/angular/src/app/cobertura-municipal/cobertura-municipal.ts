/** Tipos y funciones puras del mapa de control de cobertura (sin Angular: se prueban con vitest). */

export interface ClienteSinCoberturaDto {
  organizacionId: string;
  nombre: string;
  estado?: string | null;
  municipio?: string | null;
  cobertura: 'parcial' | 'sin_fuente' | 'seleccionado' | 'no_cubierto' | 'sin_municipio';
}

/** Cómo se reparte el catálogo (72 leyes estatales o 40 reglamentos municipales) en una ficha. */
export interface BalanceDto {
  universo: number;
  tenemos: number;
  noAplica: number;
  /** A) lo regula el estado, la federación o el municipio. */
  noAplicaPorOtraAutoridad: number;
  /** B) no existe en ese lugar o la cubre otra norma. */
  noAplicaPorNoExistir: number;
  /** C) debería existir y no se encontró el texto oficial. */
  noEncontrado: number;
  sinRevisar: number;
  avancePct: number;
  completo: boolean;
  revisado?: string | null;
}

export interface MunicipioDeCoberturaDto extends BalanceDto {
  cvegeo: string;
  nombre: string;
  poblacion: number;
  nivel: string;
}

export interface MunicipioFueraDto {
  nombre: string;
  poblacion: number;
  motivo: string;
}

export interface CoberturaDeEstadoDto {
  estado: string;
  nombre: string;
  municipios: number;
  elegidos?: number;
  poblacionCubiertaPct?: number | null;
  poblacionSeleccionadaPct?: number | null;
  cubiertos: string[];
  parciales: string[];
  sinFuente: string[];
  enProceso: string[];
  noCubiertos: string[];
  estatal?: BalanceDto;
  conFicha?: MunicipioDeCoberturaDto[];
  noElegidos?: MunicipioFueraDto[];
}

export interface CoberturaMunicipalDto {
  criterio?: string | null;
  cubiertos: number;
  parciales: number;
  sinFuente: number;
  minimoCubierto?: number;
  enProceso: number;
  noCubiertos: number;
  elegidos?: number;
  completos?: number;
  universoMunicipal?: number;
  universoEstatal?: number;
  motivosFuera?: Record<string, string>;
  clientes: ClienteSinCoberturaDto[];
  estados: CoberturaDeEstadoDto[];
  aviso?: string | null;
}

export type ClaseDeNorma = 'tenemos' | 'no_aplica' | 'no_encontrado' | 'sin_revisar';

export interface NormaDeCoberturaDto {
  id: string;
  nombre?: string | null;
  bloque?: string | null;
  clase: string;
  motivo?: string | null;
  titulo?: string | null;
  nota?: string | null;
  ultimaReforma?: string | null;
  url?: string | null;
  /** Sin reglamento propio: qué otros reglamentos del lugar lo tratan. */
  loTratan?: { nombre: string; donde?: string | null; id?: string | null }[];
}

export interface BalanceConNormasDto extends BalanceDto {
  normas: NormaDeCoberturaDto[];
}

export interface MunicipioConNormasDto extends BalanceConNormasDto {
  cvegeo: string;
  nombre: string;
  poblacion: number;
  nivel?: string | null;
}

export interface ExpedienteDeEstadoDto {
  estado: string;
  nombre: string;
  revisado?: string | null;
  estatal: BalanceConNormasDto;
  municipios: MunicipioConNormasDto[];
}

export interface PrioridadDelVigiaDto {
  municipios: string[];
  sinMunicipio: number;
  aviso?: string | null;
}

/** México en mosaico (columna, fila), 11 × 6: un cuadro por estado, aproximadamente donde está. */
export const MOSAICO: Readonly<Record<string, readonly [number, number]>> = {
  BC: [0, 0], SON: [1, 0], CHIH: [2, 0], COAH: [3, 0], NL: [4, 0],
  BCS: [0, 1], SIN: [1, 1], DGO: [2, 1], ZAC: [3, 1], SLP: [4, 1], TAMPS: [5, 1],
  NAY: [2, 2], AGS: [3, 2], GTO: [4, 2], QRO: [5, 2], HGO: [6, 2], VER: [7, 2], YUC: [10, 2],
  JAL: [3, 3], MICH: [4, 3], MEX: [5, 3], CDMX: [6, 3], TLAX: [7, 3], TAB: [8, 3], CAMP: [9, 3], QROO: [10, 3],
  COL: [4, 4], GRO: [5, 4], MOR: [6, 4], PUE: [7, 4],
  OAX: [7, 5], CHIS: [8, 5],
};

export const COLUMNAS_MOSAICO = 11;
export const FILAS_MOSAICO = 6;

/** Las celdas del mosaico en orden de lectura: el estado de cada lugar o null si va vacío. */
export function celdasDelMosaico<T extends { estado: string }>(estados: readonly T[]): (T | null)[] {
  const porLugar = new Map<string, T>();
  for (const e of estados) {
    const p = MOSAICO[e.estado];
    if (p) {
      porLugar.set(`${p[0]},${p[1]}`, e);
    }
  }
  const celdas: (T | null)[] = [];
  for (let f = 0; f < FILAS_MOSAICO; f++) {
    for (let c = 0; c < COLUMNAS_MOSAICO; c++) {
      celdas.push(porLugar.get(`${c},${f}`) ?? null);
    }
  }
  return celdas;
}

/** 0–4 por % de la población que vive en un municipio cubierto. */
export function nivelMunicipal(pct: number | null | undefined): number {
  const p = pct ?? 0;
  return p >= 80 ? 4 : p >= 55 ? 3 : p >= 35 ? 2 : p >= 15 ? 1 : 0;
}

/** 0–4 por avance de la ficha estatal: 4 completa, 0 si falta más del 30 % del catálogo. */
export function nivelEstatal(b: BalanceDto | null | undefined): number {
  if (!b || !b.universo) {
    return 0;
  }
  if (b.completo) {
    return 4;
  }
  const faltan = (b.noEncontrado + b.sinRevisar) / b.universo;
  return faltan <= 0.03 ? 3 : faltan <= 0.1 ? 2 : faltan <= 0.3 ? 1 : 0;
}

export const CLASES: readonly { clase: ClaseDeNorma; titulo: string }[] = [
  { clase: 'tenemos', titulo: 'Tenemos' },
  { clase: 'no_aplica', titulo: 'No aplica' },
  { clase: 'no_encontrado', titulo: 'No se encontró el texto oficial' },
  { clase: 'sin_revisar', titulo: 'Sin revisar' },
];

/** Las normas agrupadas por clase, en el orden de CLASES; las clases sin normas no salen. */
export function normasPorClase(normas: readonly NormaDeCoberturaDto[]): { clase: ClaseDeNorma; titulo: string; normas: NormaDeCoberturaDto[] }[] {
  return CLASES.map(c => ({ ...c, normas: normas.filter(n => n.clase === c.clase) })).filter(g => g.normas.length > 0);
}

/** Anchos (%) de la barra tenemos / no aplica / no encontrado / sin revisar. */
export function tramosDeBalance(b: BalanceDto): { clase: ClaseDeNorma; pct: number }[] {
  const u = b.universo || 1;
  return ([['tenemos', b.tenemos], ['no_aplica', b.noAplica], ['no_encontrado', b.noEncontrado], ['sin_revisar', b.sinRevisar]] as const)
    .map(([clase, n]) => ({ clase, pct: Math.round((1000 * n) / u) / 10 }));
}

/** El contenido de `_vigia/prioridad.txt` (municipios de los clientes para la revisión mensual del vigía). */
export function textoDePrioridad(p: PrioridadDelVigiaDto, fecha: string): string {
  return [
    `# Municipios de clientes activos (generado en la plataforma el ${fecha}).`,
    '# Se revisan cada mes con: python vigia_municipal.py   (Morelos va siempre).',
    ...p.municipios.filter(m => /^[A-Z]{2,5}\/[a-z0-9_]+$/.test(m)),
    '',
  ].join('\n');
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
