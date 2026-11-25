import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CoberturaMunicipalDto, ExpedienteDeEstadoDto, PrioridadDelVigiaDto } from './cobertura-municipal';

export * from './cobertura-municipal';

@Injectable({ providedIn: 'root' })
export class CoberturaMunicipalService {
  private readonly rest = inject(RestService);

  reporte(): Observable<CoberturaMunicipalDto> {
    return this.rest.request<void, CoberturaMunicipalDto>({ method: 'GET', url: '/api/app/cobertura-municipal/reporte' },
      { apiName: 'Default', skipHandleError: true });
  }

  expediente(estado: string): Observable<ExpedienteDeEstadoDto> {
    return this.rest.request<void, ExpedienteDeEstadoDto>(
      { method: 'GET', url: '/api/app/cobertura-municipal/expediente', params: { estado } },
      { apiName: 'Default', skipHandleError: true });
  }

  prioridadDelVigia(): Observable<PrioridadDelVigiaDto> {
    return this.rest.request<void, PrioridadDelVigiaDto>({ method: 'GET', url: '/api/app/cobertura-municipal/prioridad-del-vigia' },
      { apiName: 'Default', skipHandleError: true });
  }
}
