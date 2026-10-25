import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

/**
 * Lo que ABP no cuenta del usuario actual. La identidad —nombre, usuario, correo— NO viaja
 * aquí: ya está en `/api/abp/application-configuration`, que el SPA descarga al arrancar.
 * Ver `MiCuentaDto` en el servidor.
 */
export interface MiCuentaDto {
  creacionUtc: string;
  accesoAnteriorUtc?: string | null;
  correoConfirmado: boolean;
  telefono?: string | null;
  segundoFactorActivo: boolean;
  telegramVinculado: boolean;
  sesionesActivas: number;
  clavesDeApi: number;
  avisosSinLeer: number;
  roles: string[];
}

@Injectable({ providedIn: 'root' })
export class MiCuentaService {
  private readonly rest = inject(RestService);

  get(): Observable<MiCuentaDto> {
    // ⚠️ La ruta la genera ABP a partir del nombre del servicio y no se adivina (§6.2):
    // `MiCuentaAppService.GetAsync()` sale como GET /api/app/mi-cuenta. Confirmado en
    // Swagger, no deducido.
    return this.rest.request<void, MiCuentaDto>(
      { method: 'GET', url: '/api/app/mi-cuenta' },
      { apiName: 'Default' },
    );
  }
}
