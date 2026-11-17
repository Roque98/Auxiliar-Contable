import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export enum OrigenDeOperacion {
  Banco = 0,
  FueraDelBanco = 1,
}

export enum TipoDeEvidencia {
  ComprobanteDePago = 0,
  Contrato = 1,
  EntregableOFoto = 2,
  PedidoOCotizacion = 3,
  Testimonio = 4,
  Otro = 5,
}

export enum EstadoDeEvidencia {
  NoAplica = 0,
  Completa = 1,
  FaltaMaterialidad = 2,
  FaltaComprobante = 3,
}

export interface EvidenciaDto {
  id: string | null;
  tipo: TipoDeEvidencia;
  documentoId: string | null;
  nombreDelDocumento: string | null;
  nota: string | null;
  cuando: string;
}

export interface OperacionDto {
  origen: OrigenDeOperacion;
  clave: string | null;
  movimientoFueraDelBancoId: string | null;
  fecha: string | null;
  descripcion: string;
  contraparte: string | null;
  esSalida: boolean;
  importe: number;
  facturas: string[];
  estado: EstadoDeEvidencia;
  evidencias: EvidenciaDto[];
  explicacion: string | null;
}

export interface ExpedienteDelMesDto {
  anio: number;
  mes: number;
  operaciones: OperacionDto[];
  hayConciliacion: boolean;
  completas: number;
  faltaMaterialidad: number;
  faltaComprobante: number;
  puedeAgregar: boolean;
  cerrado: boolean;
}

export interface AgregarEvidenciaDto {
  anio: number;
  mes: number;
  origen: OrigenDeOperacion;
  clave: string | null;
  movimientoFueraDelBancoId: string | null;
  tipo: TipoDeEvidencia;
  documentoId: string | null;
  nota: string | null;
}

/** `ControlInternoAppService` (`/api/app/control-interno`): el expediente de cada operación del mes. */
@Injectable({ providedIn: 'root' })
export class ControlInternoService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/control-interno';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  delMes(organizacionId: string, anio: number, mes: number): Observable<ExpedienteDelMesDto> {
    return this.rest.request<void, ExpedienteDelMesDto>({ method: 'GET', url: this.base, params: { organizacionId, anio, mes } }, this.opciones);
  }

  agregar(organizacionId: string, e: AgregarEvidenciaDto): Observable<EvidenciaDto> {
    return this.rest.request<AgregarEvidenciaDto, EvidenciaDto>(
      { method: 'POST', url: `${this.base}/agregar/${organizacionId}`, body: e }, this.opciones);
  }

  quitar(organizacionId: string, evidenciaId: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.base}/quitar`, params: { organizacionId, evidenciaId } }, this.opciones);
  }
}

export const TIPOS_DE_EVIDENCIA: { tipo: TipoDeEvidencia; nombre: string; materialidad: boolean }[] = [
  { tipo: TipoDeEvidencia.Contrato, nombre: 'Contrato u orden de servicio', materialidad: true },
  { tipo: TipoDeEvidencia.EntregableOFoto, nombre: 'Entregable, reporte o foto', materialidad: true },
  { tipo: TipoDeEvidencia.PedidoOCotizacion, nombre: 'Pedido, cotización u orden de compra', materialidad: true },
  { tipo: TipoDeEvidencia.ComprobanteDePago, nombre: 'Comprobante de pago (transferencia, voucher, recibo)', materialidad: false },
  { tipo: TipoDeEvidencia.Testimonio, nombre: 'Explicación del cliente', materialidad: false },
  { tipo: TipoDeEvidencia.Otro, nombre: 'Otro', materialidad: false },
];

export const ESTADO: Record<EstadoDeEvidencia, { texto: string; clase: string }> = {
  [EstadoDeEvidencia.NoAplica]: { texto: 'No se pide', clase: 'ig-etiqueta-tinta' },
  [EstadoDeEvidencia.Completa]: { texto: 'Con evidencia', clase: 'ig-etiqueta-exito' },
  [EstadoDeEvidencia.FaltaMaterialidad]: { texto: 'Falta probar que ocurrió', clase: 'ig-etiqueta-alerta' },
  [EstadoDeEvidencia.FaltaComprobante]: { texto: 'Falta comprobante o explicación', clase: 'ig-etiqueta-error' },
};
