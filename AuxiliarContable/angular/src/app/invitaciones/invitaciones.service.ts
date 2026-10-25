import { inject, Injectable } from '@angular/core';
import { PagedResultDto, RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

/** Igual que `EstadoInvitacion` en el backend. */
export enum EstadoInvitacion {
  Pendiente = 0,
  Aceptada = 1,
  Revocada = 2,
  Vencida = 3,
}

export interface InvitacionDto {
  id: string;
  email: string;
  rol: string;
  rolEtiqueta: string;
  estado: EstadoInvitacion;
  creationTime: string;
  expiraEn?: string | null;
  ultimoEnvio?: string | null;
  envios: number;
  aceptadaEn?: string | null;
  invitadoPor?: string | null;
  enCola: boolean;
  organizacionId?: string | null;
  /** Vacío para el equipo, o si la organización se borró. */
  organizacion?: string | null;
}

export interface RolInvitableDto {
  nombre: string;
  etiqueta: string;
  nivel: number;
}

export interface OpcionesDeInvitacionDto {
  correoConfigurado: boolean;
  diasDeVigencia: number;
  roles: RolInvitableDto[];
}

export interface InvitacionPublicaDto {
  vigente: boolean;
  email?: string | null;
  rolEtiqueta?: string | null;
  usuarioSugerido?: string | null;
  organizacion?: string | null;
}

export interface AceptarInvitacionDto {
  token: string;
  userName: string;
  name?: string;
  surname?: string;
  password: string;
  /** La versión del aviso de privacidad que se le enseñó y aceptó (casilla obligatoria). */
  versionDelAvisoAceptada: string;
}

/** `InvitacionAppService` (`/api/app/invitacion`) y `RegistroAppService` (`/api/app/registro`). */
@Injectable({ providedIn: 'root' })
export class InvitacionesService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/invitacion';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  getOpciones(): Observable<OpcionesDeInvitacionDto> {
    return this.rest.request<void, OpcionesDeInvitacionDto>({ method: 'GET', url: `${this.base}/opciones` }, this.opciones);
  }

  getList(params: { filtro?: string; estado?: EstadoInvitacion | null; skipCount: number; maxResultCount: number }) {
    const limpios = Object.fromEntries(Object.entries(params).filter(([, v]) => v !== null && v !== undefined && v !== ''));
    return this.rest.request<void, PagedResultDto<InvitacionDto>>(
      { method: 'GET', url: this.base, params: limpios as Record<string, string | number> },
      this.opciones,
    );
  }

  crear(email: string, rol: string, organizacionId: string | null, rolEnOrganizacion = 1): Observable<InvitacionDto> {
    return this.rest.request<unknown, InvitacionDto>(
      { method: 'POST', url: this.base, body: { email, rol, organizacionId, rolEnOrganizacion } },
      this.opciones,
    );
  }

  reenviar(id: string): Observable<InvitacionDto> {
    return this.rest.request<void, InvitacionDto>({ method: 'POST', url: `${this.base}/${id}/reenviar` }, this.opciones);
  }

  revocar(id: string): Observable<InvitacionDto> {
    return this.rest.request<void, InvitacionDto>({ method: 'POST', url: `${this.base}/${id}/revocar` }, this.opciones);
  }

  // --- Registro (anónimo) -------------------------------------------------------

  consultar(token: string): Observable<InvitacionPublicaDto> {
    return this.rest.request<unknown, InvitacionPublicaDto>(
      { method: 'POST', url: '/api/app/registro/consultar', body: { token } },
      this.opciones,
    );
  }

  aceptar(datos: AceptarInvitacionDto): Observable<void> {
    return this.rest.request<AceptarInvitacionDto, void>(
      { method: 'POST', url: '/api/app/registro/aceptar', body: datos },
      this.opciones,
    );
  }
}

/** El mensaje que manda ABP en un error de negocio, o el de reserva. */
export function mensajeDeError(err: unknown, porDefecto = 'No se pudo completar. Inténtalo de nuevo.'): string {
  const e = err as { error?: { error?: { message?: string; validationErrors?: { message: string }[] } } };
  const validacion = e?.error?.error?.validationErrors?.map(v => v.message).join(' ');
  return validacion || e?.error?.error?.message || porDefecto;
}
