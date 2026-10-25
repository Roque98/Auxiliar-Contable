import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

/** Igual que `SemaforoDeCliente` en el backend. */
export enum SemaforoDeCliente {
  AlDia = 0,
  Atencion = 1,
  Urgente = 2,
}

export interface ClienteDeCarteraDto {
  id: string;
  nombre: string;
  rfc?: string | null;
  perfilCompleto: number;
  datosPorConfirmar: number;
  documentosDelMes: number;
  facturasDelMes: number;
  estadosDeCuentaDelMes: number;
  estadosPorRevisar: number;
  propuestasPorRevisar: number;
  movimientosSinFactura: number;
  ultimoDocumentoUtc?: string | null;
  semaforo: SemaforoDeCliente;
  pendientes: string[];
}

export interface MetricasDelDespachoDto {
  anio: number;
  mes: number;
  clientes: number;
  clientesConPerfilCompleto: number;
  clientesAlDia: number;
  documentosDelMes: number;
  facturasDelMes: number;
  estadosDeCuentaDelMes: number;
  conciliadosDelMes: number;
  movimientosSinFactura: number;
  mensajesDelChat: number;
  personasEnElChat: number;
  tokensDelChat: number;
}

/** `CarteraAppService` (`/api/app/cartera`). */
@Injectable({ providedIn: 'root' })
export class CarteraService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/cartera';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  clientes(anio: number, mes: number): Observable<ClienteDeCarteraDto[]> {
    return this.rest.request<void, ClienteDeCarteraDto[]>({ method: 'GET', url: `${this.base}/clientes`, params: { anio, mes } }, this.opciones);
  }

  metricas(anio: number, mes: number): Observable<MetricasDelDespachoDto> {
    return this.rest.request<void, MetricasDelDespachoDto>({ method: 'GET', url: `${this.base}/metricas`, params: { anio, mes } }, this.opciones);
  }
}
