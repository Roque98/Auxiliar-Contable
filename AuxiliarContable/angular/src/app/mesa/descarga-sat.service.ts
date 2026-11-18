import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface FacturasDelSatDelMesDto {
  anio: number;
  mes: number;
  facturas: number;
  canceladas: number;
  sinEstatus: number;
  danadas: number;
  sinEntregaDelCliente: boolean;
}

export interface DescargaDelSatDto {
  anio: number;
  mes: number;
  sinEntregaDelCliente: boolean;
  tieneRfc: boolean;
  meses: FacturasDelSatDelMesDto[];
  danadas: { documentoId: string; nombre: string; fecha: string | null }[];
  ultimoEstatusUtc: string | null;
}

export interface OrdenDeDescargaDto {
  nombreDeArchivo: string;
  contenido: string;
  tengo: number;
  danadas: number;
}

export interface ResultadoDelPaqueteDto {
  desde: string;
  hasta: string;
  nuevas: number;
  repetidas: number;
  reemplazadas: number;
  enMesCerrado: number;
  rechazadas: number;
  estatusPuestos: number;
  canceladas: number;
  sinXmlEnElSat: number;
  avisos: string[];
}

export interface MesDeImpuestosDto {
  periodo: string;
  facturado: number;
  notasDeCredito: number;
  cobrado: number;
  comprasFacturadas: number;
  pagado: number;
  nomina: number;
  ivaCobrado: number;
  ivaPagado: number;
  ivaRetenidoPorClientes: number;
  isrRetenidoPorClientes: number;
  ivaEstimado: number;
  cfdiEmitidos: number;
  cfdiRecibidos: number;
  metodo: string | null;
  ingresosAcumulados: number | null;
  deduccionesAcumuladas: number | null;
  deduccionesDelMes: number;
  base: number | null;
  isrCausado: number | null;
  isrRetenido: number;
  isrPagosAnteriores: number | null;
  isrAPagar: number | null;
  isrAFavor: number | null;
  ivaAcreditable: number;
  ivaResultado: number;
  ivaSaldoAnterior: number;
  ivaAPagar: number;
  ivaSaldoAFavor: number;
}

export interface PagosProvisionalesDto {
  regimen: { principal: string | null; claves: string[]; nombre: string | null; flujo: boolean; fuente: string | null };
  meses: MesDeImpuestosDto[];
  avisos: string[];
  supuestos: string[];
  errores: { archivo: string; error: string }[];
  archivo: { nombre: string; contenidoB64: string } | null;
}

export interface ImpuestosAlDiaDto {
  anio: number;
  mes: number;
  calculado: boolean;
  calculadoUtc: string | null;
  actualizando: boolean;
  error: string | null;
  facturas: number;
  canceladas: number;
  danadas: number;
  sinEstatus: number;
  sinEntregaDelCliente: boolean;
  resultado: PagosProvisionalesDto | null;
  delMes: MesDeImpuestosDto | null;
}

export interface ImpuestosDelMesDto {
  anio: number;
  mes: number;
  sinEntregaDelCliente: boolean;
  facturasEnviadas: number;
  canceladas: number;
  danadas: number;
  sinEstatus: number;
  resultado: PagosProvisionalesDto;
}

/** `DescargaDelSatAppService` (`/api/app/descarga-del-sat`): facturas bajadas del SAT con el Descargador 1 e impuesto del mes. */
@Injectable({ providedIn: 'root' })
export class DescargaSatService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/descarga-del-sat';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  estado(organizacionId: string, anio: number, mes: number): Observable<DescargaDelSatDto> {
    return this.rest.request<void, DescargaDelSatDto>({ method: 'GET', url: this.base, params: { organizacionId, anio, mes } }, this.opciones);
  }

  marcarSinEntrega(organizacionId: string, anio: number, mes: number, sinEntrega: boolean): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.base}/marcar-sin-entrega/${organizacionId}`, params: { anio, mes, sinEntrega } }, this.opciones);
  }

  orden(organizacionId: string, desdeAnio: number, desdeMes: number, hastaAnio: number, hastaMes: number, emitidas: boolean, recibidas: boolean): Observable<OrdenDeDescargaDto> {
    return this.rest.request<void, OrdenDeDescargaDto>(
      { method: 'GET', url: `${this.base}/orden/${organizacionId}`, params: { desdeAnio, desdeMes, hastaAnio, hastaMes, emitidas, recibidas } },
      this.opciones);
  }

  subirPaquete(organizacionId: string, archivo: File): Observable<ResultadoDelPaqueteDto> {
    const datos = new FormData();
    datos.append('paquete', archivo, archivo.name);
    return this.rest.request<FormData, ResultadoDelPaqueteDto>(
      { method: 'POST', url: `${this.base}/subir-paquete/${organizacionId}`, body: datos }, this.opciones);
  }

  /** `ImpuestosAlDiaAppService`: el cálculo guardado del mes (se recalcula solo al entrar facturas). */
  impuestosAlDia(organizacionId: string, anio: number, mes: number): Observable<ImpuestosAlDiaDto> {
    return this.rest.request<void, ImpuestosAlDiaDto>(
      { method: 'GET', url: '/api/app/impuestos-al-dia', params: { organizacionId, anio, mes } }, this.opciones);
  }

  calcularImpuestos(organizacionId: string, anio: number, mes: number): Observable<ImpuestosDelMesDto> {
    return this.rest.request<void, ImpuestosDelMesDto>(
      { method: 'POST', url: `${this.base}/calcular-impuestos/${organizacionId}`, params: { anio, mes } }, this.opciones);
  }
}
