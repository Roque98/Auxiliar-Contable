import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface PropertyChangeDto {
  propertyName: string;
  /** Ya viene legible: el servidor quita las comillas del JSON y convierte "null" en null. */
  originalValue?: string | null;
  newValue?: string | null;
}

export interface EntityChangeDto {
  id: string;
  changeTime: string;
  /** Created | Updated | Deleted */
  changeType: string;
  entityTypeFullName: string;
  /** Solo la última parte del nombre del tipo. */
  entityName: string;
  entityId: string;
  /** Null si el cambio ocurrió fuera de una petición (por ejemplo, un worker). */
  userName?: string | null;
  clientIpAddress?: string | null;
  properties: PropertyChangeDto[];
}

export interface PagedEntityChanges {
  totalCount: number;
  items: EntityChangeDto[];
}

export interface EntityChangeQuery {
  days: number;
  entityTypeFullName?: string;
  entityId?: string;
  userName?: string;
  skipCount: number;
  maxResultCount: number;
}

@Injectable({ providedIn: 'root' })
export class EntityChangesService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/entity-change';

  getList(q: EntityChangeQuery): Observable<PagedEntityChanges> {
    const params: Record<string, string | number> = {
      days: q.days,
      skipCount: q.skipCount,
      maxResultCount: q.maxResultCount,
    };
    if (q.entityTypeFullName) params['entityTypeFullName'] = q.entityTypeFullName;
    if (q.entityId) params['entityId'] = q.entityId;
    if (q.userName) params['userName'] = q.userName;

    return this.rest.request<void, PagedEntityChanges>(
      { method: 'GET', url: this.baseUrl, params },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  getFilters(): Observable<{ entityTypes: string[] }> {
    return this.rest.request<void, { entityTypes: string[] }>(
      { method: 'GET', url: `${this.baseUrl}/filters` },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
