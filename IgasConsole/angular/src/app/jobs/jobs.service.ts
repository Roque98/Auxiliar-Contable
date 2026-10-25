import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface JobFailureDto {
  attemptedUtc: string;
  tryCount: number;
  exceptionType: string;
  message: string;
  stackTrace?: string | null;
}

export interface JobDto {
  id: string;
  jobName: string;
  /** JSON tal cual se guardó. Nunca lleva secretos: ver la nota del DTO en el servidor. */
  jobArgs: string;
  tryCount: number;
  creationTime: string;
  nextTryTime: string;
  lastTryTime?: string | null;
  completionTime?: string | null;
  /** Esperando | Reintentando | Abandonado | Completado. Lo deriva el servidor. */
  state: string;
  lastFailure?: JobFailureDto | null;
}

export interface JobSummaryDto {
  esperando: number;
  reintentando: number;
  abandonado: number;
  completado: number;
}

@Injectable({ providedIn: 'root' })
export class JobsService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/job';

  getList(state: string | null, maxResults: number): Observable<JobDto[]> {
    const params: Record<string, string | number> = { maxResults };
    if (state) {
      params['state'] = state;
    }
    return this.rest.request<void, JobDto[]>(
      { method: 'GET', url: this.baseUrl, params },
      // Sin el permiso da 403 y la pantalla lo dice ella misma: no es un error inesperado.
      { apiName: 'Default', skipHandleError: true },
    );
  }

  getSummary(): Observable<JobSummaryDto> {
    return this.rest.request<void, JobSummaryDto>(
      { method: 'GET', url: `${this.baseUrl}/summary` },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  getFailureHistory(id: string): Observable<JobFailureDto[]> {
    return this.rest.request<void, JobFailureDto[]>(
      // El id va en la RUTA porque el parámetro se llama "id". Confirmado en Swagger.
      { method: 'GET', url: `${this.baseUrl}/${id}/failure-history` },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  retry(id: string): Observable<JobDto> {
    return this.rest.request<void, JobDto>(
      { method: 'POST', url: `${this.baseUrl}/${id}/retry` },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  delete(id: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'DELETE', url: `${this.baseUrl}/${id}` },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
