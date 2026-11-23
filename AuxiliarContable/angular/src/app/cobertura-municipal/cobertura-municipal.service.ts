import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CoberturaMunicipalDto } from './cobertura-municipal';

export * from './cobertura-municipal';

@Injectable({ providedIn: 'root' })
export class CoberturaMunicipalService {
  private readonly rest = inject(RestService);

  reporte(): Observable<CoberturaMunicipalDto> {
    return this.rest.request<void, CoberturaMunicipalDto>({ method: 'GET', url: '/api/app/cobertura-municipal/reporte' },
      { apiName: 'Default', skipHandleError: true });
  }
}
