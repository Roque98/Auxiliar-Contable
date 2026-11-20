import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface ContratoEspecializadoDto {
  id?: string | null;
  /** presta o recibe. */
  rol: string;
  contraparteRfc: string;
  contraparteNombre: string;
  objeto: string;
  inicio: string;
  fin?: string | null;
  trabajadoresAproximados: number;
  registroRepse?: string | null;
  registroVigenteHasta?: string | null;
  trabajadores: string[];
}

export interface CasillaRepseDto {
  clave: string;
  texto: string;
  fundamento: string;
  cumplida: boolean;
}

export interface ContratoDelMesDto {
  contratoId: string;
  rol: string;
  contraparte: string;
  facturas: number;
  importe: number;
  limite: string;
  casillas: CasillaRepseDto[];
  vencido: boolean;
}

export interface InformeRepseDto {
  anio: number;
  cuatrimestre: number;
  meses: string;
  limite: string;
  casillas: CasillaRepseDto[];
}

export interface RepseDelMesDto {
  anio: number;
  mes: number;
  puedeEditar: boolean;
  contratos: ContratoEspecializadoDto[];
  delMes: ContratoDelMesDto[];
  informe?: InformeRepseDto | null;
  avisos: string[];
}

export interface InformeCuatrimestralDto {
  nombre: string;
  contenidoB64: string;
  avisos: string[];
}

/** Servicios especializados (REPSE) de un cliente: contratos, revisión del mes e informe cuatrimestral (solo el despacho). */
@Injectable({ providedIn: 'root' })
export class RepseService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/repse';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  delMes(organizacionId: string, anio: number, mes: number): Observable<RepseDelMesDto> {
    return this.rest.request<void, RepseDelMesDto>({ method: 'GET', url: `${this.base}/del-mes/${organizacionId}`, params: { anio, mes } }, this.opciones);
  }

  guardarContrato(organizacionId: string, c: ContratoEspecializadoDto): Observable<ContratoEspecializadoDto> {
    return this.rest.request<ContratoEspecializadoDto, ContratoEspecializadoDto>(
      { method: 'POST', url: `${this.base}/guardar-contrato/${organizacionId}`, body: c }, this.opciones);
  }

  borrarContrato(organizacionId: string, contratoId: string): Observable<void> {
    return this.rest.request<void, void>({ method: 'POST', url: `${this.base}/borrar-contrato`, params: { organizacionId, contratoId } }, this.opciones);
  }

  marcar(organizacionId: string, m: { contratoId: string | null; anio: number; periodo: number; clave: string; cumplida: boolean }): Observable<void> {
    return this.rest.request<object, void>({ method: 'POST', url: `${this.base}/marcar/${organizacionId}`, body: m }, this.opciones);
  }

  informeCuatrimestral(organizacionId: string, anio: number, cuatrimestre: number): Observable<InformeCuatrimestralDto> {
    return this.rest.request<void, InformeCuatrimestralDto>(
      { method: 'POST', url: `${this.base}/informe-cuatrimestral/${organizacionId}`, params: { anio, cuatrimestre } }, this.opciones);
  }
}
