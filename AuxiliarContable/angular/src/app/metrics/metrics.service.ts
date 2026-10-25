import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface MetricPointDto {
  bucket: string;
  cpuPercent?: number | null;
  memoryPercent?: number | null;
  diskUsedPercent?: number | null;
  requestCount: number;
  errorCount: number;
  avgResponseMs?: number | null;
  maxResponseMs?: number | null;
  unhealthySamples: number;
  swapPercent?: number | null;
  dbSizeMb?: number | null;
  dbConnections?: number | null;
  clientErrorCount: number;
  restarts: number;
}

export interface MetricsSeriesDto {
  fromUtc: string;
  toUtc: string;
  bucketMinutes: number;
  points: MetricPointDto[];
  latest?: MetricPointDto | null;
  /** Segundos desde la última muestra real. Null si no hay ninguna. */
  secondsSinceLastSample?: number | null;
  sampleIntervalSeconds: number;
}

export interface RateLimitEventDto {
  windowStartUtc: string;
  ipAddress: string;
  endpoint: string;
  rejectionCount: number;
  firstRejectedUtc: string;
  lastRejectedUtc: string;
}

@Injectable({ providedIn: 'root' })
export class MetricsService {
  private readonly rest = inject(RestService);

  getSeries(hours: number): Observable<MetricsSeriesDto> {
    return this.rest.request<void, MetricsSeriesDto>(
      { method: 'GET', url: '/api/app/metrics/series', params: { hours } },
      // skipHandleError: esta pagina sondea cada minuto. Sin esto, cada sondeo
      // perdido (pantalla bloqueada, cambio de app, tunel movil) levanta el modal
      // global de error de ABP. Un widget que se refresca solo no debe interrumpir
      // al usuario porque una peticion de fondo no llegara.
      { apiName: 'Default', skipHandleError: true },
    );
  }

  getBlockedEvents(hours: number, maxResults = 200): Observable<RateLimitEventDto[]> {
    return this.rest.request<void, RateLimitEventDto[]>(
      { method: 'GET', url: '/api/app/metrics/blocked-events', params: { hours, maxResults } },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  exportBlockedEvents(hours: number, maxResults = 200): Observable<Blob> {
    return this.rest.request<void, Blob>(
      { method: 'GET', url: '/api/app/metrics/blocked-events-csv', params: { hours, maxResults } },
      // responseType va en la CONFIG y no en la request: RestService solo mira el de la
      // config al llamar a HttpClient. Y va como literal con 'as any' porque Rest.ResponseType
      // es un const enum de un .d.ts, que Angular no deja importar con isolatedModules.
      { apiName: 'Default', skipHandleError: true, responseType: 'blob' as any },
    );
  }
}

// --- Registro de peticiones (auditoría de ABP) -----------------------------------

export interface AuditLogEntryDto {
  executionTime: string;
  executionDuration: number;
  httpMethod?: string | null;
  url?: string | null;
  httpStatusCode?: number | null;
  clientIpAddress?: string | null;
  userName?: string | null;
  hasException: boolean;
}

@Injectable({ providedIn: 'root' })
export class AuditLogsService {
  private readonly rest = inject(RestService);

  getRecent(
    hours: number,
    minStatusCode?: number,
    ipAddress?: string,
    userName?: string,
    maxResults = 200,
  ): Observable<AuditLogEntryDto[]> {
    return this.rest.request<void, AuditLogEntryDto[]>(
      {
        method: 'GET',
        url: '/api/app/audit-logs/recent',
        params: this.parametros(hours, minStatusCode, ipAddress, userName, maxResults),
      },
      // Sin el permiso AuxiliarContable.AuditLogs da 403 y la seccion no se muestra; igual que
      // con los ajustes, un modal ahi seria un error donde solo hay una funcion que no
      // corresponde a ese usuario.
      { apiName: 'Default', skipHandleError: true },
    );
  }

  /**
   * Descarga el CSV con LOS MISMOS filtros que la tabla. Construye los parámetros con el
   * mismo método privado para que no puedan divergir: si el export trajera "todo" mientras
   * en pantalla hay un filtro puesto, quien abre el archivo se lleva una sorpresa.
   */
  exportRecent(
    hours: number,
    minStatusCode?: number,
    ipAddress?: string,
    userName?: string,
    maxResults = 200,
  ): Observable<Blob> {
    return this.rest.request<void, Blob>(
      {
        method: 'GET',
        url: '/api/app/audit-logs/recent-csv',
        params: this.parametros(hours, minStatusCode, ipAddress, userName, maxResults),
      },
      { apiName: 'Default', skipHandleError: true, responseType: 'blob' as any },
    );
  }

  private parametros(
    hours: number,
    minStatusCode?: number,
    ipAddress?: string,
    userName?: string,
    maxResults = 200,
  ): Record<string, string | number> {
    const params: Record<string, string | number> = { hours, maxResults };
    if (minStatusCode !== undefined) {
      params['minStatusCode'] = minStatusCode;
    }
    if (ipAddress?.trim()) {
      params['ipAddress'] = ipAddress.trim();
    }
    if (userName?.trim()) {
      params['userName'] = userName.trim();
    }
    return params;
  }
}

// --- Configuración de los avisos -------------------------------------------------

export interface MonitoringSettingsDto {
  alertsEnabled: boolean;
  /** El backend nunca devuelve el token real, solo si hay uno y sus últimos caracteres. */
  hasBotToken: boolean;
  botTokenHint?: string | null;
  chatIds?: string | null;
  /** Resultado del último intento de envío. Null = todavía no se intentó ninguno. */
  lastAlertUtc?: string | null;
  lastAlertOk?: boolean | null;
  lastAlertDetail?: string | null;
}

export interface UpdateMonitoringSettingsDto {
  /**
   * Omitido/null = no tocar el interruptor. Se manda SOLO cuando el usuario lo acciona;
   * al guardar el token no viaja, porque el valor que tuviera la página podría estar
   * obsoleto y apagaría los avisos sin que nadie lo pidiera.
   */
  alertsEnabled?: boolean | null;
  /** Vacío = conservar el token guardado. */
  botToken?: string | null;
  chatIds?: string | null;
}

export interface SendTestAlertResultDto {
  success: boolean;
  message?: string | null;
}

@Injectable({ providedIn: 'root' })
export class MonitoringSettingsService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/monitoring-settings';

  get(): Observable<MonitoringSettingsDto> {
    return this.rest.request<void, MonitoringSettingsDto>(
      { method: 'GET', url: this.baseUrl },
      // Sin permiso da 403; la seccion simplemente no se muestra. Un modal ahi seria
      // un error para el usuario donde solo hay una funcion que no le corresponde.
      { apiName: 'Default', skipHandleError: true },
    );
  }

  update(body: UpdateMonitoringSettingsDto): Observable<MonitoringSettingsDto> {
    return this.rest.request<UpdateMonitoringSettingsDto, MonitoringSettingsDto>(
      { method: 'PUT', url: this.baseUrl, body },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  sendTest(): Observable<SendTestAlertResultDto> {
    return this.rest.request<void, SendTestAlertResultDto>(
      { method: 'POST', url: this.baseUrl + '/send-test' },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
