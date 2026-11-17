import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { OperacionesDelMesDto } from '../mi-informacion/operaciones.service';

export interface MesaDelMesDto {
  operaciones: OperacionesDelMesDto;
  porAnalizar: number;
  analizados: number;
  conciliadaUtc: string | null;
  movimientosDelBanco: number;
  movimientosComoOperacion: number;
}

export interface ResultadoDeAnalisisDto {
  analizados: number;
  faltan: number;
  completadas: number;
  avisos: string[];
}

export interface SugerenciaDeAmarreDto {
  movimientoId: string;
  movimiento: string;
  operacionId: string;
  operacion: string;
  confianza: string;
  porque: string;
}

export interface ResultadoDeAmarreDto {
  operacionesNuevas: number;
  porUuid: number;
  porRegla: number;
  porClaude: number;
  sugerencias: SugerenciaDeAmarreDto[];
  avisos: string[];
}

export interface MovimientoContableDto {
  cuenta: string;
  nombre: string;
  debe: number;
  haber: number;
  texto: string;
}

export interface PolizaContableDto {
  operacion: string;
  tipo: string;
  numero: number;
  fecha: string;
  concepto: string;
  uuid: string | null;
  categoria: string | null;
  movimientos: MovimientoContableDto[];
}

export interface RenglonDeRevisionDto {
  operacion: string | null;
  codigo: string;
  nivel: 'detiene' | 'aviso' | 'aceptado';
  mensaje: string;
}

export interface DatosDeCierreDto {
  inventarioInicial: number | null;
  inventarioFinal: number | null;
  costoVendido: number | null;
}

export interface PolizasDelMesDto {
  anio: number;
  mes: number;
  generadoUtc: string;
  detenidas: number;
  cierre: DatosDeCierreDto | null;
  inventarioInicialSugerido: number | null;
  resultado: {
    polizas: PolizaContableDto[];
    revision: RenglonDeRevisionDto[];
    balanza: { cuenta: string; nombre: string; debe: number; haber: number }[];
    pendientesDeCierre: string[];
    errores: { archivo: string; error: string }[];
  };
}

/** `MesaDeTrabajoAppService` (`/api/app/mesa-de-trabajo`): el mes de un cliente para el despacho. */
@Injectable({ providedIn: 'root' })
export class MesaService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/mesa-de-trabajo';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  delMes(organizacionId: string, anio: number, mes: number): Observable<MesaDelMesDto> {
    return this.rest.request<void, MesaDelMesDto>({ method: 'GET', url: this.base, params: { organizacionId, anio, mes } }, this.opciones);
  }

  analizar(organizacionId: string, anio: number, mes: number): Observable<ResultadoDeAnalisisDto> {
    return this.rest.request<void, ResultadoDeAnalisisDto>(
      { method: 'POST', url: `${this.base}/analizar/${organizacionId}`, params: { anio, mes } }, this.opciones);
  }

  polizas(organizacionId: string, anio: number, mes: number): Observable<PolizasDelMesDto | null> {
    return this.rest.request<void, PolizasDelMesDto | null>(
      { method: 'GET', url: `${this.base}/polizas/${organizacionId}`, params: { anio, mes } }, this.opciones);
  }

  generarPolizas(organizacionId: string, anio: number, mes: number, cierre: DatosDeCierreDto): Observable<PolizasDelMesDto> {
    return this.rest.request<DatosDeCierreDto, PolizasDelMesDto>(
      { method: 'POST', url: `${this.base}/generar-polizas/${organizacionId}`, params: { anio, mes }, body: cierre }, this.opciones);
  }

  aceptarRevision(organizacionId: string, operacionId: string, codigo: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.base}/aceptar-revision`, params: { organizacionId, operacionId, codigo } }, this.opciones);
  }

  amarrarBanco(organizacionId: string, anio: number, mes: number): Observable<ResultadoDeAmarreDto> {
    return this.rest.request<void, ResultadoDeAmarreDto>(
      { method: 'POST', url: `${this.base}/amarrar-banco/${organizacionId}`, params: { anio, mes } }, this.opciones);
  }
}
