import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Igual que `EstadoDePata` en el backend. */
export enum EstadoDePata {
  Pendiente = 0,
  Ligada = 1,
  NoHay = 2,
}

/** Igual que `FormaDeTransaccion`. */
export enum FormaDeTransaccion {
  Banco = 0,
  Efectivo = 1,
  Credito = 2,
}

/** Igual que `SentidoDeOperacion`. */
export enum SentidoDeOperacion {
  Salida = 0,
  Entrada = 1,
}

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
}

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
