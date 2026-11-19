import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Lo que Claude interpretó de una operación (motor /operaciones/interpretar). */
export interface InterpretacionDeOperacionMotorDto {
  objeto: string;
  conceptos: {
    indice: number;
    queEs: string;
    claveCoincide: boolean | null;
    categoria: string;
    cuenta: string;
    isr: string;
    iva: string;
    porcentajeInversion: number | null;
    fundamento: string;
  }[];
  proveedor: { queVende: string; loConozco: boolean };
  coincidenLasFuentes: string;
  diferencias: string[];
  categoria: string;
  juicioContable: string;
  tratamientoFiscal: string;
  alertas: { tipo: string; mensaje: string; fundamento: string }[];
  preguntasAlCliente: string[];
  confianza: string;
}

export interface InterpretacionDeOperacionDto {
  operacionId: string;
  enLaFila: boolean;
  alDia: boolean;
  interpretadaUtc: string | null;
  error: string | null;
  fuentes: string[];
  interpretacion: InterpretacionDeOperacionMotorDto | null;
}

export interface InterpretacionesDelMesDto {
  anio: number;
  mes: number;
  pendientes: number;
  enLaFila: number;
  operaciones: InterpretacionDeOperacionDto[];
}

/** `InterpretacionesAppService` (`/api/app/interpretaciones`): Claude interpreta cada operación. Solo el despacho. */
@Injectable({ providedIn: 'root' })
export class InterpretacionesService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/interpretaciones';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  delMes(organizacionId: string, anio: number, mes: number): Observable<InterpretacionesDelMesDto> {
    return this.rest.request<void, InterpretacionesDelMesDto>(
      { method: 'GET', url: `${this.base}/del-mes/${organizacionId}`, params: { anio, mes } }, this.opciones);
  }

  /** Pone en la fila las del mes que no están al día (o solo una, aunque lo esté). */
  pedir(organizacionId: string, anio: number, mes: number, operacionId?: string): Observable<InterpretacionesDelMesDto> {
    const params: Record<string, string | number> = { anio, mes };
    if (operacionId) {
      params['operacionId'] = operacionId;
    }
    return this.rest.request<void, InterpretacionesDelMesDto>(
      { method: 'POST', url: `${this.base}/pedir/${organizacionId}`, params }, this.opciones);
  }
}

/** Cómo se lee cada valor del tratamiento. */
export const TEXTO_ISR: Record<string, string> = {
  deducible: 'deducible', deducible_parcial: 'deducible en parte', no_deducible: 'no deducible', inversion: 'inversión',
  costo_de_lo_vendido: 'costo de lo vendido', deduccion_personal: 'deducción personal', ingreso_acumulable: 'ingreso acumulable',
  ingreso_no_acumulable: 'ingreso no acumulable', no_aplica: 'no aplica',
};
export const TEXTO_IVA: Record<string, string> = {
  acreditable: 'IVA acreditable', acreditable_parcial: 'IVA acreditable en parte', no_acreditable: 'IVA no acreditable',
  trasladado: 'IVA trasladado', exento_o_tasa_0: 'exento o tasa 0', no_aplica: 'sin IVA',
};
export const TEXTO_FUENTES: Record<string, string> = {
  factura: 'factura', ticket_o_nota: 'ticket o nota', movimiento_del_banco: 'estado de cuenta',
};
export const TEXTO_ALERTA: Record<string, string> = {
  requisito_fiscal: 'Requisito fiscal', no_deducible: 'No deducible', posible_error: 'Posible error',
  otra_interpretacion: 'Pudo ser otra cosa', falta_informacion: 'Falta información',
};
