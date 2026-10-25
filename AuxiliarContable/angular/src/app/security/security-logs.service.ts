import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface SecurityLogDto {
  id: string;
  creationTime: string;
  /** Nombre que escribe ABP: LoginSucceeded, LoginFailed, ChangePassword… */
  action: string;
  identity: string;
  userName?: string | null;
  clientIpAddress?: string | null;
  browserInfo?: string | null;
  clientId?: string | null;
  /** Lo deriva el servidor: la pantalla no tiene que conocer los nombres de ABP. */
  isFailure: boolean;
  /** Lo que la entrada guardó aparte de las columnas fijas, ya en texto. Casi siempre vacío. */
  detalle?: string | null;
}

export interface PagedSecurityLogs {
  totalCount: number;
  items: SecurityLogDto[];
}

export interface SecurityLogFiltersDto {
  actions: string[];
}

export interface SecurityLogQuery {
  days: number;
  action?: string;
  userName?: string;
  clientIpAddress?: string;
  skipCount: number;
  maxResultCount: number;
}

@Injectable({ providedIn: 'root' })
export class SecurityLogsService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/security-log';

  getList(q: SecurityLogQuery): Observable<PagedSecurityLogs> {
    const params: Record<string, string | number> = {
      days: q.days,
      skipCount: q.skipCount,
      maxResultCount: q.maxResultCount,
    };
    // Solo se mandan los filtros con valor: una cadena vacía significaría "que contenga
    // la cadena vacía", no "sin filtro".
    if (q.action) params['actionName'] = q.action;
    if (q.userName) params['userName'] = q.userName;
    if (q.clientIpAddress) params['clientIpAddress'] = q.clientIpAddress;

    return this.rest.request<void, PagedSecurityLogs>(
      { method: 'GET', url: this.baseUrl, params },
      // Sin el permiso da 403 y la sección no se muestra: no es un error del usuario.
      { apiName: 'Default', skipHandleError: true },
    );
  }

  getFilters(): Observable<SecurityLogFiltersDto> {
    return this.rest.request<void, SecurityLogFiltersDto>(
      { method: 'GET', url: `${this.baseUrl}/filters` },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
