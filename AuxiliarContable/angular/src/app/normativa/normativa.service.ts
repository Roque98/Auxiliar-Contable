import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface PedirAnalisisNormativoDto {
  pregunta: string;
  fechaDeLaOperacion?: string | null;
  contraparteTipoDePersona?: 'fisica' | 'moral' | null;
  contraparteResidencia?: string | null;
  disparadores: string[];
}

export interface AnalisisNormativoDto {
  id: string;
  pregunta: string;
  fechaDeLaOperacion?: string | null;
  contraparteTipoDePersona?: string | null;
  contraparteResidencia?: string | null;
  disparadores: string[];
  estado: 'Pendiente' | 'Listo' | 'Error';
  conclusion?: string | null;
  confianza?: string | null;
  error?: string | null;
  creationTime: string;
  listoUtc?: string | null;
  /** JSON del motor: { analisis, expediente }. */
  resultado?: string | null;
}

export interface DisparadorDto {
  clave: string;
  descripcion: string;
}


export { leerResultado } from './resultado';
export type { AnalisisDelMotor, ExpedienteDelMotor, ResultadoNormativo } from './resultado';

@Injectable({ providedIn: 'root' })
export class NormativaService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/normativa';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  pedir(organizacionId: string, input: PedirAnalisisNormativoDto): Observable<AnalisisNormativoDto> {
    return this.rest.request<PedirAnalisisNormativoDto, AnalisisNormativoDto>(
      { method: 'POST', url: `${this.base}/pedir/${organizacionId}`, body: input }, this.opciones);
  }

  lista(organizacionId: string): Observable<AnalisisNormativoDto[]> {
    return this.rest.request<void, AnalisisNormativoDto[]>({ method: 'GET', url: `${this.base}/consultas/${organizacionId}` }, this.opciones);
  }

  analisis(organizacionId: string, id: string): Observable<AnalisisNormativoDto> {
    return this.rest.request<void, AnalisisNormativoDto>(
      { method: 'GET', url: `${this.base}/${id}/analisis/${organizacionId}` }, this.opciones);
  }

  disparadores(): Observable<DisparadorDto[]> {
    return this.rest.request<void, DisparadorDto[]>({ method: 'GET', url: `${this.base}/disparadores` }, this.opciones);
  }
}
