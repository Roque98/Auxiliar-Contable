import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Igual que `TipoDeMovimientoFueraDelBanco` en el backend. */
export enum TipoDeMovimiento {
  EntradaEnEfectivo = 0,
  SalidaEnEfectivo = 1,
  CompraACredito = 2,
  VentaACredito = 3,
}

export interface MovimientoDto {
  id: string;
  tipo: TipoDeMovimiento;
  fecha: string;
  importe: number;
  concepto: string;
  contraparte: string | null;
  documentoId: string | null;
  nombreDelDocumento: string | null;
  vence: string | null;
  saldado: string | null;
  nota: string | null;
  pendiente: boolean;
  vencido: boolean;
  deOtroMes: boolean;
}

export interface DineroDelMesDto {
  anio: number;
  mes: number;
  movimientos: MovimientoDto[];
  entradasEnEfectivo: number;
  salidasEnEfectivo: number;
  porPagar: number;
  porCobrar: number;
  puedeEditar: boolean;
  cerrado: boolean;
}

export interface GuardarMovimientoDto {
  tipo: TipoDeMovimiento;
  fecha: string;
  importe: number;
  concepto: string;
  contraparte: string | null;
  documentoId: string | null;
  vence: string | null;
  nota: string | null;
}

/** `DineroFueraDelBancoAppService` (`/api/app/dinero-fuera-del-banco`): efectivo y crédito del cliente. */
@Injectable({ providedIn: 'root' })
export class DineroService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/dinero-fuera-del-banco';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  delMes(organizacionId: string, anio: number, mes: number): Observable<DineroDelMesDto> {
    return this.rest.request<void, DineroDelMesDto>({ method: 'GET', url: this.base, params: { organizacionId, anio, mes } }, this.opciones);
  }

  crear(organizacionId: string, m: GuardarMovimientoDto): Observable<MovimientoDto> {
    return this.rest.request<GuardarMovimientoDto, MovimientoDto>(
      { method: 'POST', url: `${this.base}/crear/${organizacionId}`, body: m }, this.opciones);
  }

  actualizar(organizacionId: string, movimientoId: string, m: GuardarMovimientoDto): Observable<MovimientoDto> {
    return this.rest.request<GuardarMovimientoDto, MovimientoDto>(
      { method: 'POST', url: `${this.base}/actualizar`, params: { organizacionId, movimientoId }, body: m }, this.opciones);
  }

  borrar(organizacionId: string, movimientoId: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.base}/borrar`, params: { organizacionId, movimientoId } }, this.opciones);
  }

  saldar(organizacionId: string, movimientoId: string, fecha: string | null): Observable<MovimientoDto> {
    return this.rest.request<void, MovimientoDto>(
      { method: 'POST', url: `${this.base}/saldar`, params: fecha ? { organizacionId, movimientoId, fecha } : { organizacionId, movimientoId } },
      this.opciones);
  }
}

export const TIPOS: { tipo: TipoDeMovimiento; nombre: string; ayuda: string; credito: boolean }[] = [
  { tipo: TipoDeMovimiento.EntradaEnEfectivo, nombre: 'Entrada en efectivo', ayuda: 'Una venta cobrada en efectivo o el corte de caja del día.', credito: false },
  { tipo: TipoDeMovimiento.SalidaEnEfectivo, nombre: 'Salida en efectivo', ayuda: 'Un gasto o una compra pagada en efectivo.', credito: false },
  { tipo: TipoDeMovimiento.CompraACredito, nombre: 'Compra a crédito', ayuda: 'Algo que se compró y todavía no se paga.', credito: true },
  { tipo: TipoDeMovimiento.VentaACredito, nombre: 'Venta a crédito', ayuda: 'Algo que se vendió y todavía no se cobra.', credito: true },
];
