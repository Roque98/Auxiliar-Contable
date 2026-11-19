import { RenglonDto } from './renglones.component';

/** Estados financieros del ejercicio hasta el mes, como los manda el motor (NIF B-6 y B-3). */
export interface EstadosFinancierosDto {
  desde: string;
  hasta: string;
  resultado: RenglonDeEstadoDto[];
  situacion: RenglonDeEstadoDto[];
  cuadre: number | null;
  preliminar: boolean;
  advertencias: string[];
  razones: { clave: string; nombre: string; valor: number | null; unidad: string; lectura: string }[];
  /** Borrador de notas (NIF B-6, B-3 y particulares). */
  notas?: { titulo: string; texto: string }[];
  /** Revelaciones obligatorias de las NIF que aplican (lista de verificación). */
  revelaciones?: { norma: string; titulo: string; parrafo: string; texto: string }[];
}

export interface RenglonDeEstadoDto {
  concepto: string;
  importe: number | null;
  fuente: string;
  tipo?: string | null;
}

/** «B-3 52.1.1 a)» -> «NIF B-3 52.1.1 a)». */
function norma(fuente: string): string {
  return /^[A-D]-\d/.test(fuente) ? `NIF ${fuente}` : fuente;
}

const FINALES = ['= Utilidad (pérdida) neta', '= Resultado integral'];

/** Estado de resultados: sin los renglones en cero (quedan los totales «= …»). */
export function renglonesDeResultado(ef: EstadosFinancierosDto): RenglonDto[] {
  return ef.resultado
    .filter(r => r.concepto.startsWith('=') || Math.abs(r.importe ?? 0) >= 0.005)
    .map(r => ({
      concepto: r.concepto,
      importe: r.importe,
      fundamento: norma(r.fuente),
      tipo: FINALES.includes(r.concepto) ? 'resultado' : r.concepto.startsWith('=') ? 'subtotal' : 'renglon',
    }));
}

/** Estado de situación financiera: títulos, rubros, subtotales y totales. */
export function renglonesDeSituacion(ef: EstadosFinancierosDto): RenglonDto[] {
  const tipo: Record<string, string> = { titulo: 'titulo', rubro: 'renglon', subtotal: 'subtotal', total: 'resultado' };
  return ef.situacion.map(r => ({ concepto: r.concepto, importe: r.importe, fundamento: norma(r.fuente), tipo: tipo[r.tipo ?? ''] ?? 'renglon' }));
}

/** «2026-01» -> «enero 2026». */
export function mesLargo(periodo: string, meses: readonly string[]): string {
  const [a, m] = periodo.split('-').map(Number);
  return a && m ? `${meses[m - 1]} ${a}` : periodo;
}
