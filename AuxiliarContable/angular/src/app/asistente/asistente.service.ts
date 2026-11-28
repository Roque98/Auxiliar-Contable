import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

/** Igual que `RolDelMensaje` en el backend (los de herramientas no llegan aquí). */
export enum RolDelMensaje {
  Usuario = 1,
  Asistente = 2,
}

export interface MensajeDto {
  id: string;
  rol: RolDelMensaje;
  texto: string;
  creadoUtc: string;
  acciones: string[];
  /** Los archivos que armó en ese turno. */
  archivos?: ArchivoDelAsistenteDto[];
  /** Trae una tabla: se puede bajar en Excel o PDF. */
  tieneTabla?: boolean;
}

export interface ArchivoDelAsistenteDto {
  mensajeId: string;
  uso: string;
  nombre: string;
}

export interface ConversacionDto {
  organizacionId: string;
  nombreDelCliente: string;
  disponible: boolean;
  puedeEditar: boolean;
  esDelDespacho: boolean;
  mensajes: MensajeDto[];
}

/** La conversación de toda la cartera (solo equipo): `Guid.Empty` en el backend. */
export const CARTERA = '00000000-0000-0000-0000-000000000000';

/** Igual que `ConversacionConsts.MaxTextoDelUsuario`. */
export const MAX_TEXTO = 4000;

/** `AsistenteAppService` (`/api/app/asistente`). */
@Injectable({ providedIn: 'root' })
export class AsistenteService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/asistente';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  obtener(organizacionId: string): Observable<ConversacionDto> {
    return this.rest.request<void, ConversacionDto>({ method: 'GET', url: this.base, params: { organizacionId } }, this.opciones);
  }

  enviar(organizacionId: string, texto: string): Observable<ConversacionDto> {
    return this.rest.request<{ texto: string }, ConversacionDto>(
      { method: 'POST', url: `${this.base}/enviar/${organizacionId}`, body: { texto } },
      this.opciones,
    );
  }

  /** El archivo que armó el asistente (`uso`) o una respuesta suya en `formato` (excel, pdf…). */
  descargar(mensajeId: string, uso: string | null, formato: string | null): Observable<Blob> {
    const params: Record<string, string> = { mensajeId };
    if (uso) {
      params['uso'] = uso;
    }
    if (formato) {
      params['formato'] = formato;
    }
    return this.rest.request<void, Blob>({ method: 'GET', url: `${this.base}/archivo`, params, responseType: 'blob' }, this.opciones);
  }

  empezarDeNuevo(organizacionId: string): Observable<ConversacionDto> {
    return this.rest.request<void, ConversacionDto>(
      { method: 'POST', url: `${this.base}/empezar-de-nuevo/${organizacionId}` },
      this.opciones,
    );
  }
}
