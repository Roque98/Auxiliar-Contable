import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { FacturaDeLaOperacion } from './guia';

// Los enums viven en guia.ts (sin Angular, para las pruebas); iguales que en el backend.
import { EstadoDePata, FormaDeTransaccion, SentidoDeOperacion } from './guia';
export { EstadoDePata, FormaDeTransaccion, SentidoDeOperacion };

export interface PataDto {
  estado: EstadoDePata;
  referencia: string | null;
  documentoId: string | null;
  nombreDelDocumento: string | null;
  motivo: string | null;
  forma: FormaDeTransaccion | null;
  movimientoFueraDelBancoId: string | null;
  vence: string | null;
  saldado: string | null;
  solicitudDeFacturaId: string | null;
  /** Solo transacción: es un movimiento del estado de cuenta conciliado. */
  delBanco?: boolean;
  /** Lo que Claude leyó del documento de esta pata. */
  analisis?: AnalisisCortoDto | null;
}

export interface AnalisisCortoDto {
  legible: boolean;
  queEs: string;
  relacionConGiro: string;
  explicacion: string;
  quePrueba: string;
  confianza: string;
}

/** Cómo se dice cada categoría que da Claude (igual que «Mis facturas»). */
export const NOMBRES_DE_CATEGORIA: Record<string, string> = {
  mercancia: 'Mercancía para revender',
  insumo_directo: 'Materia prima o insumo',
  gasto_indirecto: 'Gasto indirecto',
  gasto_operacion: 'Gasto de operación',
  inversion: 'Inversión (activo fijo)',
  deduccion_personal: 'Deducción personal',
  ajeno: 'Ajeno al giro',
  venta: 'Venta',
  no_aplica: 'No es una operación',
};

export interface OperacionDto {
  id: string;
  fecha: string;
  sentido: SentidoDeOperacion;
  importe: number | null;
  descripcion: string;
  contraparte: string | null;
  factura: PataDto;
  transaccion: PataDto;
  controlInterno: PataDto;
  ligadas: number;
  respondidas: number;
}

export interface OperacionesDelMesDto {
  anio: number;
  mes: number;
  operaciones: OperacionDto[];
  puedeEditar: boolean;
  cerrado: boolean;
}

export type { FacturaDeLaOperacion };

/** Lo que el cliente dijo que subiría después (`GetPorSubir`). */
export interface PorSubirDto {
  operacionId: string;
  fecha: string;
  descripcion: string;
  importe: number | null;
  /** 0 factura, 1 transacción, 2 control interno. */
  pata: number;
  motivo: string;
  dias: number;
}

export interface GuardarPataDto {
  estado: EstadoDePata;
  referencia?: string | null;
  documentoId?: string | null;
  motivo?: string | null;
  forma?: FormaDeTransaccion | null;
  vence?: string | null;
}

export interface GuardarOperacionDto {
  fecha: string;
  sentido: SentidoDeOperacion;
  importe: number | null;
  descripcion: string;
  contraparte: string | null;
  factura?: GuardarPataDto | null;
  transaccion?: GuardarPataDto | null;
  controlInterno?: GuardarPataDto | null;
}

/** Lo que se contesta con un toque cuando una pata no existe. */
export const MOTIVOS = {
  factura: ['No se facturó', 'No cuento con ella'],
  transaccion: ['No tengo la referencia o folio', 'No tengo el comprobante de la transferencia'],
  controlInterno: ['No tengo comprobante interno'],
} as const;

/** `OperacionesAppService` (`/api/app/operaciones`): factura · transacción · control interno de cada operación. */
@Injectable({ providedIn: 'root' })
export class OperacionesService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/operaciones';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  delMes(organizacionId: string, anio: number, mes: number): Observable<OperacionesDelMesDto> {
    return this.rest.request<void, OperacionesDelMesDto>({ method: 'GET', url: this.base, params: { organizacionId, anio, mes } }, this.opciones);
  }

  deDocumento(organizacionId: string, documentoId: string): Observable<OperacionDto> {
    return this.rest.request<void, OperacionDto>(
      { method: 'POST', url: `${this.base}/de-documento`, params: { organizacionId, documentoId } }, this.opciones);
  }

  deSolicitud(organizacionId: string, solicitudId: string): Observable<OperacionDto> {
    return this.rest.request<void, OperacionDto>(
      { method: 'POST', url: `${this.base}/de-solicitud`, params: { organizacionId, solicitudId } }, this.opciones);
  }

  crear(organizacionId: string, o: GuardarOperacionDto): Observable<OperacionDto> {
    return this.rest.request<GuardarOperacionDto, OperacionDto>(
      { method: 'POST', url: `${this.base}/crear/${organizacionId}`, body: o }, this.opciones);
  }

  actualizar(organizacionId: string, operacionId: string, o: GuardarOperacionDto): Observable<OperacionDto> {
    return this.rest.request<GuardarOperacionDto, OperacionDto>(
      { method: 'POST', url: `${this.base}/actualizar`, params: { organizacionId, operacionId }, body: o }, this.opciones);
  }

  juntar(organizacionId: string, operacionId: string, conOperacionId: string): Observable<OperacionDto> {
    return this.rest.request<void, OperacionDto>(
      { method: 'POST', url: `${this.base}/juntar`, params: { organizacionId, operacionId, conOperacionId } }, this.opciones);
  }

  borrar(organizacionId: string, operacionId: string): Observable<void> {
    return this.rest.request<void, void>({ method: 'POST', url: `${this.base}/borrar`, params: { organizacionId, operacionId } }, this.opciones);
  }

  /** Lo que dice el XML de la factura de la operación (para la guía); null si no tiene XML. */
  factura(organizacionId: string, operacionId: string): Observable<FacturaDeLaOperacion | null> {
    return this.rest.request<void, FacturaDeLaOperacion | null>(
      { method: 'GET', url: `${this.base}/factura`, params: { organizacionId, operacionId } }, this.opciones);
  }

  /** Lo que el cliente dijo que subiría después, para recordárselo. */
  porSubir(organizacionId: string): Observable<PorSubirDto[]> {
    return this.rest.request<void, PorSubirDto[]>({ method: 'GET', url: `${this.base}/por-subir/${organizacionId}` }, this.opciones);
  }
}

/** Los datos de una operación tal como se mandan a guardar (sin patas: cada acción agrega la suya). */
export function datosDe(o: OperacionDto): GuardarOperacionDto {
  return {
    fecha: o.fecha.substring(0, 10),
    sentido: o.sentido,
    importe: o.importe,
    descripcion: o.descripcion,
    contraparte: o.contraparte,
  };
}
