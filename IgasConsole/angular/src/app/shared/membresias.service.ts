import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface OrganizacionResumenDto {
  id: string;
  nombre: string;
  /** Quien pregunta puede subir aquí (el equipo, siempre; un cliente, según su papel). */
  puedeSubir: boolean;
}

export interface OpcionesDeOrganizacionDto {
  organizaciones: OrganizacionResumenDto[];
  /** Si quien mira puede crear una organización desde el formulario. */
  puedeCrear: boolean;
  /** A partir de este nivel de rol no hace falta organización (el equipo). */
  nivelSinOrganizacion: number;
}

export interface MembresiaDeUsuarioDto {
  userId: string;
  organizaciones: OrganizacionResumenDto[];
}

/**
 * `MembresiaAppService`: las organizaciones vistas desde Usuarios e Invitaciones. La regla
 * que las hace necesarias —un cliente se da de alta DENTRO de una organización— vive en el
 * backend (`MembresiasDeOrganizacion`); esto solo ayuda a cumplirla sin sorpresas.
 */
@Injectable({ providedIn: 'root' })
export class MembresiasService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/membresia';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  getOpciones(): Observable<OpcionesDeOrganizacionDto> {
    return this.rest.request<void, OpcionesDeOrganizacionDto>({ method: 'GET', url: `${this.base}/opciones` }, this.opciones);
  }

  deUsuarios(userIds: string[]): Observable<MembresiaDeUsuarioDto[]> {
    return this.rest.request<void, MembresiaDeUsuarioDto[]>(
      { method: 'GET', url: `${this.base}/de-usuarios`, params: { userIds } },
      this.opciones,
    );
  }
}

/** ¿Un rol de este nivel entra como cliente, y por tanto necesita organización? */
export function necesitaOrganizacion(nivel: number, opciones: OpcionesDeOrganizacionDto | null): boolean {
  return nivel < (opciones?.nivelSinOrganizacion ?? 1);
}
