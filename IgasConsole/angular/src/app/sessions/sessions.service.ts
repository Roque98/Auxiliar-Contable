import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface UserSessionDto {
  /** Id de la FILA. El identificador real de sesión no se expone. */
  id: string;
  device?: string | null;
  deviceInfo?: string | null;
  ipAddresses?: string | null;
  signedIn: string;
  lastAccessed?: string | null;
  /** True si es la sesión desde la que se está mirando la lista. */
  isCurrent: boolean;
}

@Injectable({ providedIn: 'root' })
export class SessionsService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/user-session';

  getList(): Observable<UserSessionDto[]> {
    return this.rest.request<void, UserSessionDto[]>(
      { method: 'GET', url: this.baseUrl },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  revoke(id: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.baseUrl}/${id}/revoke` },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  revokeOthers(): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.baseUrl}/revoke-others` },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
