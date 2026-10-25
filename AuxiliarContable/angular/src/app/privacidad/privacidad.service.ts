import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface EstadoDePrivacidadDto {
  versionVigente: string;
  aceptada: boolean;
}

@Injectable({ providedIn: 'root' })
export class PrivacidadService {
  private readonly rest = inject(RestService);
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  estado(): Observable<EstadoDePrivacidadDto> {
    return this.rest.request<void, EstadoDePrivacidadDto>(
      { method: 'GET', url: '/api/app/privacidad/estado' },
      this.opciones,
    );
  }

  aceptar(version: string): Observable<void> {
    return this.rest.request<{ version: string }, void>(
      { method: 'POST', url: '/api/app/privacidad/aceptar', body: { version } },
      this.opciones,
    );
  }
}
