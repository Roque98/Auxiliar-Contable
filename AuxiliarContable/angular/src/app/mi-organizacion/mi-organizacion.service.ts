import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';
import { RolEnOrganizacion } from '../shared/papeles';

export interface MiembroDeMiOrganizacionDto {
  userId: string;
  userName: string;
  nombreCompleto?: string | null;
  email?: string | null;
  rol: RolEnOrganizacion;
}

export interface InvitacionPendienteDto {
  id: string;
  email: string;
  rol: RolEnOrganizacion;
  creationTime: string;
}

export interface MiOrganizacionDto {
  id: string;
  nombre: string;
  miRol: RolEnOrganizacion;
  puedoGestionarPersonas: boolean;
  miembros: MiembroDeMiOrganizacionDto[];
  pendientes: InvitacionPendienteDto[];
}

/**
 * `MiOrganizacionAppService` por HTTP. Las rutas son las que publica ABP (ver Swagger): el
 * id de la organización va en la ruta solo en «invitar»; en lo demás, en la consulta.
 */
@Injectable({ providedIn: 'root' })
export class MiOrganizacionService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/mi-organizacion';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  getList(): Observable<MiOrganizacionDto[]> {
    return this.rest.request<void, MiOrganizacionDto[]>({ method: 'GET', url: this.base }, this.opciones);
  }

  invitar(organizacionId: string, email: string, rol: RolEnOrganizacion): Observable<MiOrganizacionDto> {
    return this.rest.request<{ email: string; rol: RolEnOrganizacion }, MiOrganizacionDto>(
      { method: 'POST', url: `${this.base}/invitar/${organizacionId}`, body: { email, rol } },
      this.opciones,
    );
  }

  cambiarRol(organizacionId: string, userId: string, rol: RolEnOrganizacion): Observable<MiOrganizacionDto> {
    return this.rest.request<{ rol: RolEnOrganizacion }, MiOrganizacionDto>(
      { method: 'POST', url: `${this.base}/cambiar-rol`, params: { organizacionId, userId }, body: { rol } },
      this.opciones,
    );
  }

  quitar(organizacionId: string, userId: string): Observable<MiOrganizacionDto> {
    return this.rest.request<void, MiOrganizacionDto>(
      { method: 'POST', url: `${this.base}/quitar`, params: { organizacionId, userId } },
      this.opciones,
    );
  }

  revocarInvitacion(organizacionId: string, invitacionId: string): Observable<MiOrganizacionDto> {
    return this.rest.request<void, MiOrganizacionDto>(
      { method: 'POST', url: `${this.base}/revocar-invitacion`, params: { organizacionId, invitacionId } },
      this.opciones,
    );
  }
}
