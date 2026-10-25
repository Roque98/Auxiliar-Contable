import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

/** Igual que `RevisionDelEstadoDeCuenta` en el backend. */
export enum RevisionDelEstadoDeCuenta {
  PorConfirmar = 0,
  RequiereRevision = 1,
  Confirmado = 2,
}

export interface EstadoDeCuentaResumenDto {
  id: string;
  documentoId: string;
  banco: string;
  cuenta?: string | null;
  periodoInicio?: string | null;
  periodoFin?: string | null;
  moneda: string;
  saldoInicial?: number | null;
  saldoFinal?: number | null;
  totalAbonos: number;
  totalCargos: number;
  diferencia?: number | null;
  cuadra: boolean;
  saltosDeSaldo: number;
  movimientos: number;
  revision: RevisionDelEstadoDeCuenta;
}

export interface MovimientoBancarioDto {
  id: string;
  orden: number;
  fecha: string;
  concepto: string;
  referencia?: string | null;
  cargo: number;
  abono: number;
  saldo?: number | null;
  saltoDeSaldo: boolean;
}

export interface EstadoDeCuentaDto extends EstadoDeCuentaResumenDto {
  puedeEditar: boolean;
  renglones: MovimientoBancarioDto[];
}

/** `EstadosDeCuentaAppService` (`/api/app/estados-de-cuenta`). */
@Injectable({ providedIn: 'root' })
export class EstadosDeCuentaService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/estados-de-cuenta';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  lista(organizacionId: string): Observable<EstadoDeCuentaResumenDto[]> {
    return this.rest.request<void, EstadoDeCuentaResumenDto[]>({ method: 'GET', url: this.base, params: { organizacionId } }, this.opciones);
  }

  obtener(organizacionId: string, estadoDeCuentaId: string): Observable<EstadoDeCuentaDto> {
    return this.rest.request<void, EstadoDeCuentaDto>(
      { method: 'GET', url: `${this.base}/detalle`, params: { organizacionId, estadoDeCuentaId } },
      this.opciones,
    );
  }

  confirmar(organizacionId: string, estadoDeCuentaId: string, aunqueNoCuadre: boolean): Observable<EstadoDeCuentaDto> {
    return this.rest.request<void, EstadoDeCuentaDto>(
      { method: 'POST', url: `${this.base}/confirmar`, params: { organizacionId, estadoDeCuentaId, aunqueNoCuadre } },
      this.opciones,
    );
  }

  corregirMovimiento(organizacionId: string, estadoDeCuentaId: string, m: MovimientoBancarioDto): Observable<EstadoDeCuentaDto> {
    return this.rest.request<unknown, EstadoDeCuentaDto>(
      {
        method: 'POST',
        url: `${this.base}/corregir-movimiento`,
        params: { organizacionId, estadoDeCuentaId },
        body: { movimientoId: m.id, fecha: m.fecha, concepto: m.concepto, cargo: m.cargo, abono: m.abono },
      },
      this.opciones,
    );
  }

  corregirSaldos(organizacionId: string, estadoDeCuentaId: string, saldoInicial: number | null, saldoFinal: number | null): Observable<EstadoDeCuentaDto> {
    return this.rest.request<unknown, EstadoDeCuentaDto>(
      { method: 'POST', url: `${this.base}/corregir-saldos`, params: { organizacionId, estadoDeCuentaId }, body: { saldoInicial, saldoFinal } },
      this.opciones,
    );
  }

  volverALeer(organizacionId: string, documentoId: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.base}/volver-aLeer`, params: { organizacionId, documentoId } },
      this.opciones,
    );
  }
}
