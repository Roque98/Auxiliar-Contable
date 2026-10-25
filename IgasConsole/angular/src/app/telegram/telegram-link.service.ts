import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface TelegramLinkStatusDto {
  isLinked: boolean;
  linkedUtc?: string | null;
  telegramUserName?: string | null;
  /** False si no hay bot configurado: sin bot no hay nada que vincular. */
  botAvailable: boolean;
  /** Si al iniciar sesión se pide además un código enviado por Telegram. */
  twoFactorEnabled: boolean;
}

export interface StartTelegramLinkResultDto {
  /** Lleva el token en claro y solo se devuelve UNA vez: no se puede volver a consultar. */
  deepLink: string;
  expiresUtc: string;
  validMinutes: number;
}

@Injectable({ providedIn: 'root' })
export class TelegramLinkService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/telegram-link';

  getStatus(): Observable<TelegramLinkStatusDto> {
    return this.rest.request<void, TelegramLinkStatusDto>(
      { method: 'GET', url: this.baseUrl + '/status' },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  start(): Observable<StartTelegramLinkResultDto> {
    return this.rest.request<void, StartTelegramLinkResultDto>(
      { method: 'POST', url: this.baseUrl + '/start' },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  setTwoFactor(enabled: boolean): Observable<TelegramLinkStatusDto> {
    return this.rest.request<void, TelegramLinkStatusDto>(
      // set-two-factor y no two-factor: la ruta la genera ABP a partir del nombre del
      // método (SetTwoFactorAsync). Confirmado en Swagger, no deducido.
      { method: 'POST', url: `${this.baseUrl}/set-two-factor?enabled=${enabled}` },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  unlink(): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: this.baseUrl + '/unlink' },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
