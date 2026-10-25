import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { BehaviorSubject, Observable, tap } from 'rxjs';

export interface NotificationDto {
  id: string;
  /** Info | Warning | Error */
  severity: string;
  title: string;
  body: string;
  url?: string | null;
  creationTime: string;
  isRead: boolean;
}

@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/notification';

  /**
   * El número de la campana, compartido.
   *
   * Vive en el servicio y no en el componente porque hay DOS sitios que lo cambian: la
   * campana (que refresca sola) y la pantalla (donde se marca leído). Con un contador por
   * componente, marcar todo como leído dejaría la campana con el número viejo hasta la
   * siguiente vuelta del reloj.
   */
  private readonly sinLeer = new BehaviorSubject<number>(0);
  readonly sinLeer$ = this.sinLeer.asObservable();

  getList(onlyUnread: boolean, maxResults: number): Observable<NotificationDto[]> {
    return this.rest.request<void, NotificationDto[]>(
      { method: 'GET', url: this.baseUrl, params: { onlyUnread, maxResults } },
      // Un anónimo o una sesión caducada dan 401: la campana simplemente no se pinta, no
      // es un error que merezca un modal.
      { apiName: 'Default', skipHandleError: true },
    );
  }

  refrescarContador(): Observable<number> {
    return this.rest
      .request<void, number>(
        { method: 'GET', url: `${this.baseUrl}/unread-count` },
        { apiName: 'Default', skipHandleError: true },
      )
      .pipe(tap(n => this.sinLeer.next(n ?? 0)));
  }

  markRead(ids: string[]): Observable<void> {
    return this.rest.request<{ ids: string[] }, void>(
      { method: 'POST', url: `${this.baseUrl}/mark-read`, body: { ids } },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  markAllRead(): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.baseUrl}/mark-all-read` },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
