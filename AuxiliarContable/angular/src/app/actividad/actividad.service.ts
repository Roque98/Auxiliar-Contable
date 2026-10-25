import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export type TipoDeActividad = 'sesion' | 'reporte' | 'organizacion' | 'personas';

export interface ActividadDto {
  fecha: string;
  usuarioId: string;
  usuario: string;
  nombreCompleto?: string | null;
  organizaciones: string[];
  tipo: TipoDeActividad;
  texto: string;
  veces: number;
  ip?: string | null;
}

export interface PaginaDeActividadDto {
  items: ActividadDto[];
  siguienteAntesDe?: string | null;
}

export interface OpcionesDeActividadDto {
  personas: { id: string; usuario: string; nombreCompleto?: string | null }[];
  organizaciones: { id: string; nombre: string }[];
  diasDeRegistro: number;
}

export interface FiltroDeActividad {
  desde?: string | null;
  antesDe?: string | null;
  usuarioId?: string | null;
  organizacionId?: string | null;
  tipo?: TipoDeActividad | null;
}

/** `ActividadAppService` (`/api/app/actividad`, rutas confirmadas en Swagger). */
@Injectable({ providedIn: 'root' })
export class ActividadService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/actividad';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  getList(filtro: FiltroDeActividad): Observable<PaginaDeActividadDto> {
    const params: Record<string, string> = {};
    for (const [k, v] of Object.entries(filtro)) {
      if (v) {
        params[k] = v;
      }
    }
    return this.rest.request<void, PaginaDeActividadDto>({ method: 'GET', url: this.base, params }, this.opciones);
  }

  getOpciones(): Observable<OpcionesDeActividadDto> {
    return this.rest.request<void, OpcionesDeActividadDto>({ method: 'GET', url: `${this.base}/opciones` }, this.opciones);
  }
}
