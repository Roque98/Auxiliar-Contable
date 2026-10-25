import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { BehaviorSubject, Observable, map, tap } from 'rxjs';

export interface EstadoDeSeguridadDto {
  correo?: string | null;
  correoConfirmado: boolean;
  telefono?: string | null;
  telefonoConfirmado: boolean;
  segundoFactorActivo: boolean;
  telegramVinculado: boolean;
  correoDisponible: boolean;
  smsDisponible: boolean;
  minutosDeValidez: number;
  pendientes: number;
  codigosDeRecuperacionRestantes: number;
}

export interface SeguridadDeUsuarioDto {
  segundoFactorActivo: boolean;
  correoConfirmado: boolean;
  sinCorreo: boolean;
  telefonoConfirmado: boolean;
  codigosDeRecuperacionRestantes: number;
}

/**
 * `SeguridadDeCuentaAppService` (`/api/app/seguridad-de-cuenta`). Guarda el último estado
 * para que el aviso de la barra superior y la pantalla digan lo mismo sin pedirlo dos veces.
 */
@Injectable({ providedIn: 'root' })
export class SeguridadService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/seguridad-de-cuenta';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  readonly estado$ = new BehaviorSubject<EstadoDeSeguridadDto | null>(null);

  cargar(): Observable<EstadoDeSeguridadDto> {
    return this.rest
      .request<void, EstadoDeSeguridadDto>({ method: 'GET', url: this.base }, this.opciones)
      .pipe(tap(e => this.estado$.next(e)));
  }

  enviarCodigoCorreo(correo: string): Observable<void> {
    return this.rest.request<unknown, void>({ method: 'POST', url: `${this.base}/enviar-codigo-correo`, body: { correo } }, this.opciones);
  }

  confirmarCorreo(correo: string, codigo: string): Observable<EstadoDeSeguridadDto> {
    return this.rest
      .request<unknown, EstadoDeSeguridadDto>({ method: 'POST', url: `${this.base}/confirmar-correo`, body: { correo, codigo } }, this.opciones)
      .pipe(tap(e => this.estado$.next(e)));
  }

  enviarCodigoTelefono(telefono: string): Observable<void> {
    return this.rest.request<unknown, void>({ method: 'POST', url: `${this.base}/enviar-codigo-telefono`, body: { telefono } }, this.opciones);
  }

  confirmarTelefono(telefono: string, codigo: string): Observable<EstadoDeSeguridadDto> {
    return this.rest
      .request<unknown, EstadoDeSeguridadDto>({ method: 'POST', url: `${this.base}/confirmar-telefono`, body: { telefono, codigo } }, this.opciones)
      .pipe(tap(e => this.estado$.next(e)));
  }

  guardarTelefono(telefono: string): Observable<EstadoDeSeguridadDto> {
    return this.rest
      .request<unknown, EstadoDeSeguridadDto>({ method: 'POST', url: `${this.base}/guardar-telefono`, body: { telefono } }, this.opciones)
      .pipe(tap(e => this.estado$.next(e)));
  }

  segundoFactor(activo: boolean): Observable<EstadoDeSeguridadDto> {
    return this.rest
      .request<unknown, EstadoDeSeguridadDto>({ method: 'POST', url: `${this.base}/set-segundo-factor`, body: { activo } }, this.opciones)
      .pipe(tap(e => this.estado$.next(e)));
  }

  /** Diez códigos nuevos (los anteriores dejan de valer). Se enseñan una sola vez. */
  generarCodigosDeRecuperacion(): Observable<string[]> {
    return this.rest
      .request<void, { codigos: string[] }>(
        { method: 'POST', url: `${this.base}/generar-codigos-de-recuperacion` },
        this.opciones,
      )
      .pipe(map(r => r.codigos ?? []));
  }
}

/** `SeguridadDeUsuariosAppService`: la seguridad de OTRA persona, para quien la gestiona. */
@Injectable({ providedIn: 'root' })
export class SeguridadDeUsuariosService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/seguridad-de-usuarios';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  get(id: string): Observable<SeguridadDeUsuarioDto> {
    return this.rest.request<void, SeguridadDeUsuarioDto>({ method: 'GET', url: `${this.base}/${id}` }, this.opciones);
  }

  quitarSegundoFactor(id: string): Observable<SeguridadDeUsuarioDto> {
    return this.rest.request<void, SeguridadDeUsuarioDto>(
      { method: 'POST', url: `${this.base}/${id}/quitar-segundo-factor` },
      this.opciones,
    );
  }
}
