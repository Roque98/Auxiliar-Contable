// Lo puro de la sección de servicios especializados (sin Angular, se prueba con vitest).
import type { ContratoDelMesDto, ContratoEspecializadoDto } from './repse.service';

export type EstadoRepse = 'sin-facturas' | 'completo' | 'pendiente' | 'vencido';

/** Cómo va un contrato en el mes: sin facturas no hay nada que juntar; si faltan casillas, pendiente o vencido. */
export function estadoDelContrato(c: ContratoDelMesDto): EstadoRepse {
  if (c.casillas.every(x => x.cumplida)) {
    return 'completo';
  }
  if (!c.facturas) {
    return 'sin-facturas';
  }
  return c.vencido ? 'vencido' : 'pendiente';
}

export const TEXTO_DEL_ESTADO: Record<EstadoRepse, string> = {
  'sin-facturas': 'Sin facturas en el mes',
  completo: 'Completo',
  pendiente: 'Pendiente',
  vencido: 'Vencido: no deducible y complementaria de IVA',
};

export function contratoNuevo(hoy = new Date()): ContratoEspecializadoDto {
  return {
    id: null, rol: 'recibe', contraparteRfc: '', contraparteNombre: '', objeto: '', inicio: hoy.toISOString().slice(0, 10),
    fin: null, trabajadoresAproximados: 1, registroRepse: null, registroVigenteHasta: null, trabajadores: [],
  };
}

/** RFC de persona moral (12) o física (13): letras, fecha y homoclave. */
export function rfcValido(rfc: string): boolean {
  return /^[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}$/.test((rfc ?? '').trim().toUpperCase());
}
