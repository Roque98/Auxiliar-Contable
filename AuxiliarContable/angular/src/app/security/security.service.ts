import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface BlockedIpDto {
  id: string;
  ipAddress: string;
  reason: string;
  creationTime: string;
  expiresUtc?: string | null;
  /** True si lo puso el evaluador automático, false si lo puso una persona. */
  isAutomatic: boolean;
  /** Lo calcula el servidor: la pantalla no replica la regla ni depende del reloj local. */
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class BlockedIpsService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/blocked-ip';

  getList(): Observable<BlockedIpDto[]> {
    return this.rest.request<void, BlockedIpDto[]>(
      { method: 'GET', url: this.baseUrl },
      // Sin el permiso da 403 y la seccion no se muestra: no es un error del usuario.
      { apiName: 'Default', skipHandleError: true },
    );
  }

  block(ipAddress: string, reason: string, hours?: number): Observable<BlockedIpDto> {
    const body: Record<string, unknown> = { ipAddress, reason };
    if (hours) {
      body['hours'] = hours;
    }
    return this.rest.request<Record<string, unknown>, BlockedIpDto>(
      { method: 'POST', url: `${this.baseUrl}/block`, body },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  unblock(id: string): Observable<void> {
    return this.rest.request<void, void>(
      // El id va en la RUTA, no como parámetro: ABP lo coloca ahí porque se llama "id".
      // Confirmado en Swagger, no deducido.
      { method: 'POST', url: `${this.baseUrl}/${id}/unblock` },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
