import { EtapaDelPeriodo } from '../conciliacion/conciliacion.service';
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
  ultimoDocumentoUtc?: string | null;
  semaforo: SemaforoDeCliente;
  pendientes: string[];
  /** En qué va el cierre del mes (la misma regla que la pantalla del cierre). */
  etapa: EtapaDelPeriodo;
  partidasPendientes: number;
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
  mensajesDelChat: number;
  personasEnElChat: number;
  tokensDelChat: number;
  /** Lo que costó Claude en el motor por cliente (dólares equivalentes de claude -p); null si el motor no respondió. */
  gastoDeClaude?: { organizacionId: string | null; nombre: string; llamadas: number; usd: number; lecturaUsd: number }[] | null;
  gastoDeClaudeTotalUsd?: number | null;
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
