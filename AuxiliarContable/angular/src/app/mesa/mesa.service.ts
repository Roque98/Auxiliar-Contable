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

  amarrarBanco(organizacionId: string, anio: number, mes: number): Observable<ResultadoDeAmarreDto> {
    return this.rest.request<void, ResultadoDeAmarreDto>(
      { method: 'POST', url: `${this.base}/amarrar-banco/${organizacionId}`, params: { anio, mes } }, this.opciones);
  }
}
