import { inject, Injectable } from '@angular/core';
import { ListResultDto, RestService } from '@abp/ng.core';
import { RolEnOrganizacion } from '../shared/papeles';
import { Observable } from 'rxjs';

export interface OrganizacionDto {
  id: string;
  nombre: string;
  descripcion?: string | null;
  miembros: number;
}

export interface MiembroDto {
  userId: string;
  userName: string;
  nombreCompleto?: string | null;
  email?: string | null;
  /** Su papel en la organización (en los candidatos no aplica). */
  rol?: RolEnOrganizacion;
}

export interface OrganizacionDetalleDto extends OrganizacionDto {
  listaDeMiembros: MiembroDto[];
}

export interface GuardarOrganizacionDto {
  nombre: string;
  descripcion?: string | null;
}

/** `OrganizacionAppService` por HTTP (`/api/app/organizacion`, rutas confirmadas en Swagger). */
@Injectable({ providedIn: 'root' })
export class OrganizacionesService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/organizacion';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  getList(filtro: string): Observable<ListResultDto<OrganizacionDto>> {
    const params: Record<string, string> = filtro.trim() ? { filtro: filtro.trim() } : {};
    return this.rest.request<void, ListResultDto<OrganizacionDto>>({ method: 'GET', url: this.base, params }, this.opciones);
  }

  get(id: string): Observable<OrganizacionDetalleDto> {
    return this.rest.request<void, OrganizacionDetalleDto>({ method: 'GET', url: `${this.base}/${id}` }, this.opciones);
  }

  create(body: GuardarOrganizacionDto): Observable<OrganizacionDetalleDto> {
    return this.rest.request<GuardarOrganizacionDto, OrganizacionDetalleDto>({ method: 'POST', url: this.base, body }, this.opciones);
  }

  update(id: string, body: GuardarOrganizacionDto): Observable<OrganizacionDetalleDto> {
    return this.rest.request<GuardarOrganizacionDto, OrganizacionDetalleDto>(
      { method: 'PUT', url: `${this.base}/${id}`, body },
      this.opciones,
    );
  }

  delete(id: string): Observable<void> {
    return this.rest.request<void, void>({ method: 'DELETE', url: `${this.base}/${id}` }, this.opciones);
  }

  agregarMiembro(id: string, userId: string): Observable<OrganizacionDetalleDto> {
    return this.rest.request<{ userId: string }, OrganizacionDetalleDto>(
      { method: 'POST', url: `${this.base}/${id}/agregar-miembro`, body: { userId } },
      this.opciones,
    );
  }

  cambiarRolDeMiembro(id: string, userId: string, rol: RolEnOrganizacion): Observable<OrganizacionDetalleDto> {
    return this.rest.request<{ rol: RolEnOrganizacion }, OrganizacionDetalleDto>(
      // id y userId van en la RUTA (así lo publica ABP; ver Swagger).
      { method: 'POST', url: `${this.base}/${id}/cambiar-rol-de-miembro/${userId}`, body: { rol } },
      this.opciones,
    );
  }

  quitarMiembro(id: string, userId: string): Observable<OrganizacionDetalleDto> {
    return this.rest.request<void, OrganizacionDetalleDto>(
      // El userId va en la RUTA: así lo publica ABP (confirmado en Swagger).
      { method: 'POST', url: `${this.base}/${id}/quitar-miembro/${userId}` },
      this.opciones,
    );
  }

  candidatos(id: string, filtro: string): Observable<MiembroDto[]> {
    const params: Record<string, string> = filtro.trim() ? { filtro: filtro.trim() } : {};
    return this.rest.request<void, MiembroDto[]>({ method: 'GET', url: `${this.base}/${id}/candidatos`, params }, this.opciones);
  }
}
