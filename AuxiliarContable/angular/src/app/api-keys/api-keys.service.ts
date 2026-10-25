import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface ApiKeyDto {
  id: string;
  name: string;
  /** Primeros caracteres. Nunca la clave entera. */
  pista: string;
  creationTime: string;
  expiresUtc?: string | null;
  lastUsedUtc?: string | null;
  revokedUtc?: string | null;
  isUsable: boolean;
}

export interface CreatedApiKeyDto {
  key: ApiKeyDto;
  /** Se recibe UNA sola vez: de ella el servidor solo guarda el hash. */
  plainKey: string;
}

@Injectable({ providedIn: 'root' })
export class ApiKeysService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/api-key';

  getList(): Observable<ApiKeyDto[]> {
    return this.rest.request<void, ApiKeyDto[]>(
      { method: 'GET', url: this.baseUrl },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  create(name: string, days?: number): Observable<CreatedApiKeyDto> {
    const body: Record<string, unknown> = { name };
    if (days) {
      body['days'] = days;
    }
    return this.rest.request<Record<string, unknown>, CreatedApiKeyDto>(
      { method: 'POST', url: this.baseUrl, body },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  revoke(id: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.baseUrl}/${id}/revoke` },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
