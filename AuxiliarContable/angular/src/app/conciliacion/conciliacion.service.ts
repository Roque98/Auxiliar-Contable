import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Lo que devuelve el motor fiscal (motores-fiscales), tal cual: ver ResultadosDelMotor.cs. */
export interface RenglonDeCuadre {
  concepto: string;
  declarado: number | null;
  calculado: number | null;
  diferencia: number | null;
  /** null = el banco no imprime ese dato. */
  ok: boolean | null;
  pista: string;
}

export interface CuentaLeida {
  archivo: string;
  banco: string;
  tipo: string;
  cuenta: string;
  clabe: string;
  periodoInicial: string | null;
  periodoFinal: string | null;
  cuadre: RenglonDeCuadre[];
  hallazgos: { movimiento: number | null; nivel: string; mensaje: string; sugerencia: string }[];
  cuadra: boolean;
  todosSeguros: boolean;
}

export type EstadoDeMovimiento = 'conciliado' | 'combinacion' | 'traspaso' | 'sin_factura' | 'pendiente';

export interface MovimientoLeido {
  n: number | null;
  fecha: string | null;
  concepto: string;
  referencia: string;
  naturaleza: 'abono' | 'cargo';
  importe: number;
  saldo: number | null;
  seguro: boolean;
  observaciones: string;
  cuenta: string | null;
  banco: string | null;
  conciliacion: { estado: EstadoDeMovimiento; facturas: string[]; probabilidad: number | null; motivo: string } | null;
}

export interface FacturaDelMotor {
  uuid: string | null;
  naturaleza: string | null;
  tercero: string | null;
  rfcTercero: string | null;
  serieFolio: string | null;
  tipoCfdi: string | null;
  fecha: string | null;
  importe: number | null;
  forma: string | null;
  motivo: string | null;
}

export interface ResultadoDeConciliacion {
  cuentas: CuentaLeida[];
  movimientos: MovimientoLeido[];
  facturas: FacturaDelMotor[];
  facturasSinMovimiento: FacturaDelMotor[];
  alertas: { uuid: string | null; movimiento: number | null; mensaje: string }[];
  errores: { archivo: string; error: string }[];
}

export interface ConciliacionDelMesDto {
  anio: number;
  mes: number;
  rfc: string;
  estadosDeCuenta: number;
  facturas: number;
  resultado: ResultadoDeConciliacion;
}

/** `BancosAppService` (`/api/app/bancos`). Cada llamada la calcula el motor: no hay nada guardado. */
@Injectable({ providedIn: 'root' })
export class ConciliacionService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/bancos';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  conciliar(organizacionId: string, anio: number, mes: number): Observable<ConciliacionDelMesDto> {
    return this.rest.request<void, ConciliacionDelMesDto>(
      { method: 'POST', url: `${this.base}/conciliar/${organizacionId}`, params: { anio, mes } },
      this.opciones,
    );
  }
}

export const NOMBRE_DE_ESTADO: Record<EstadoDeMovimiento, string> = {
  conciliado: 'Con factura',
  combinacion: 'Con varias facturas',
  traspaso: 'Traspaso propio',
  sin_factura: 'Sin factura',
  pendiente: 'Pendiente',
};
