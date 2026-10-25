import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface MovimientoDelBancoDto {
  id: string;
  fecha: string;
  banco: string;
  concepto: string;
  cargo: number;
  abono: number;
  sinFacturaPorque?: string | null;
}

export interface FacturaDelMesDto {
  id: string;
  fecha: string;
  esVenta: boolean;
  contraparte: string;
  rfcContraparte: string;
  total: number;
  metodoDePago?: string | null;
  uuid: string;
}

export interface ParejaDto {
  id: string;
  puntaje: number;
  motivo: string;
  movimiento: MovimientoDelBancoDto;
  factura: FacturaDelMesDto;
}

export interface ConciliacionDelMesDto {
  organizacionId: string;
  anio: number;
  mes: number;
  puedeEditar: boolean;
  estadosSinRevisar: number;
  hayEstadosDeCuenta: boolean;
  propuestas: ParejaDto[];
  confirmadas: ParejaDto[];
  depositosSinFactura: MovimientoDelBancoDto[];
  cargosSinFactura: MovimientoDelBancoDto[];
  sinFacturaJustificados: MovimientoDelBancoDto[];
  ventasSinDeposito: FacturaDelMesDto[];
  gastosSinCargo: FacturaDelMesDto[];
}

/** `ConciliacionAppService` (`/api/app/conciliacion`). */
@Injectable({ providedIn: 'root' })
export class ConciliacionService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/conciliacion';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  delMes(organizacionId: string, anio: number, mes: number): Observable<ConciliacionDelMesDto> {
    return this.rest.request<void, ConciliacionDelMesDto>(
      { method: 'GET', url: `${this.base}/del-mes/${organizacionId}`, params: { anio, mes } },
      this.opciones,
    );
  }

  proponer(organizacionId: string, anio: number, mes: number): Observable<ConciliacionDelMesDto> {
    return this.rest.request<void, ConciliacionDelMesDto>(
      { method: 'POST', url: `${this.base}/proponer/${organizacionId}`, params: { anio, mes } },
      this.opciones,
    );
  }

  confirmar(organizacionId: string, conciliacionId: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.base}/confirmar`, params: { organizacionId, conciliacionId } },
      this.opciones,
    );
  }

  rechazar(organizacionId: string, conciliacionId: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.base}/rechazar`, params: { organizacionId, conciliacionId } },
      this.opciones,
    );
  }

  aMano(organizacionId: string, movimientoId: string, cfdiId: string): Observable<void> {
    return this.rest.request<unknown, void>(
      { method: 'POST', url: `${this.base}/conciliar-aMano/${organizacionId}`, body: { movimientoId, cfdiId } },
      this.opciones,
    );
  }

  sinFactura(organizacionId: string, movimientoId: string, porque: string | null): Observable<void> {
    return this.rest.request<unknown, void>(
      { method: 'POST', url: `${this.base}/marcar-sin-factura/${organizacionId}`, body: { movimientoId, porque } },
      this.opciones,
    );
  }
}
