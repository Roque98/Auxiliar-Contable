import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ConciliacionIntegralDto } from './integral';

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
  /** El método de siempre no la pudo comprobar y la leyó Claude. */
  leidaConClaude: boolean;
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
  conciliadaUtc: string;
  resultado: ResultadoDeConciliacion;
}

/** En qué va el cierre del mes (EtapaDelPeriodo). */
export enum EtapaDelPeriodo {
  FaltanDocumentos = 0,
  PorConciliar = 1,
  ConPendientes = 2,
  ListoParaCerrar = 3,
  Cerrado = 4,
}

export const NOMBRE_DE_ETAPA: Record<EtapaDelPeriodo, string> = {
  [EtapaDelPeriodo.FaltanDocumentos]: 'Faltan documentos',
  [EtapaDelPeriodo.PorConciliar]: 'Por conciliar',
  [EtapaDelPeriodo.ConPendientes]: 'Con pendientes',
  [EtapaDelPeriodo.ListoParaCerrar]: 'Listo para cerrar',
  [EtapaDelPeriodo.Cerrado]: 'Cerrado',
};

/** Qué decidió el despacho de un movimiento sin factura (ResolucionDePartida). */
export enum ResolucionDePartida {
  SinResolver = 0,
  PreguntarAlCliente = 1,
  TraspasoPropio = 2,
  SinFactura = 3,
  FacturaPorLlegar = 4,
  Revisado = 5,
}

export const NOMBRE_DE_RESOLUCION: Record<ResolucionDePartida, string> = {
  [ResolucionDePartida.SinResolver]: 'Sin resolver',
  [ResolucionDePartida.PreguntarAlCliente]: 'Preguntarle al cliente',
  [ResolucionDePartida.TraspasoPropio]: 'Traspaso entre sus cuentas',
  [ResolucionDePartida.SinFactura]: 'Real, sin factura (préstamo, no deducible…)',
  [ResolucionDePartida.FacturaPorLlegar]: 'Su factura está por llegar',
  [ResolucionDePartida.Revisado]: 'No lleva factura (comisión, interés…)',
};

export interface PartidaDto {
  id: string;
  anio: number;
  mes: number;
  clave: string;
  descripcion: string;
  resolucion: ResolucionDePartida;
  nota: string | null;
  respuesta: string | null;
  respondidaUtc: string | null;
  /** ¿Detiene el cierre? */
  pendiente: boolean;
}

export interface CierreDelMesDto {
  anio: number;
  mes: number;
  etapa: EtapaDelPeriodo;
  revision: { texto: string; hecho: boolean; bloquea: boolean }[];
  estadosDeCuenta: number;
  facturas: number;
  conciliacion: ConciliacionDelMesDto | null;
  conciliacionDesactualizada: boolean;
  partidas: PartidaDto[];
  cerradoUtc: string | null;
  /** El despacho: resuelve partidas, cierra y reabre. */
  puedeCerrar: boolean;
}

/**
 * `BancosAppService` (`/api/app/bancos`) y `CierreAppService` (`/api/app/cierre`). Conciliar lo hace
 * el motor y se guarda (una por mes); el cierre dice en qué va el mes.
 */
/** Un CFDI tal como lo nombra el diagnóstico de facturas. */
export interface CfdiDelDiagnostico {
  uuid: string;
  tipo: string | null;
  fecha: string | null;
  serieFolio: string | null;
  rfcEmisor: string | null;
  rfcReceptor: string | null;
  nombreEmisor: string | null;
  nombreReceptor: string | null;
  metodo: string | null;
  total: number | null;
  falta: boolean;
}

export interface ApartadoPorRevisar {
  fecha: string | null;
  /** Positiva: falta por saldar; negativa: se aplicó de más. */
  diferencia: number;
  facturas: CfdiDelDiagnostico[];
  pagos: CfdiDelDiagnostico[];
  egresos: CfdiDelDiagnostico[];
  avisos: string[];
}

export interface LadoDelDiagnostico {
  apartadosQueCuadran: number;
  porRevisar: ApartadoPorRevisar[];
  sueltos: {
    facturasPpdSinNada: CfdiDelDiagnostico[];
    pagosSinDocumento: CfdiDelDiagnostico[];
    egresosSinRelacion: CfdiDelDiagnostico[];
  };
}

/** `BancosAppService.DiagnosticarFacturas`: lo que no cuadra entre facturas, complementos y notas de crédito. */
export interface DiagnosticoDeFacturas {
  emitidas: LadoDelDiagnostico;
  recibidas: LadoDelDiagnostico;
  errores: { archivo: string; error: string }[];
  nota: string | null;
}

@Injectable({ providedIn: 'root' })
export class ConciliacionService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/bancos';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  /** El botón «Conciliar»: todo en una vuelta de fondo (facturas, banco, pólizas, impuestos, estados, normativa). */
  conciliarTodo(organizacionId: string, anio: number, mes: number): Observable<ConciliacionIntegralDto> {
    return this.rest.request<void, ConciliacionIntegralDto>(
      { method: 'POST', url: `/api/app/conciliacion-integral/pedir/${organizacionId}`, params: { anio, mes } }, this.opciones);
  }

  /** La última vuelta del cliente (null si nunca se ha pedido). */
  ultimaConciliacion(organizacionId: string): Observable<ConciliacionIntegralDto | null> {
    return this.rest.request<void, ConciliacionIntegralDto | null>(
      { method: 'GET', url: `/api/app/conciliacion-integral/ultima/${organizacionId}` }, this.opciones);
  }

  conciliar(organizacionId: string, anio: number, mes: number): Observable<ConciliacionDelMesDto> {
    return this.rest.request<void, ConciliacionDelMesDto>(
      { method: 'POST', url: `${this.base}/conciliar/${organizacionId}`, params: { anio, mes } },
      this.opciones,
    );
  }

  diagnosticarFacturas(organizacionId: string, anio: number, mes: number): Observable<DiagnosticoDeFacturas> {
    return this.rest.request<void, DiagnosticoDeFacturas>(
      { method: 'POST', url: `${this.base}/diagnosticar-facturas/${organizacionId}`, params: { anio, mes } },
      this.opciones,
    );
  }

  cierre(organizacionId: string, anio: number, mes: number): Observable<CierreDelMesDto> {
    return this.rest.request<void, CierreDelMesDto>(
      { method: 'GET', url: '/api/app/cierre', params: { organizacionId, anio, mes } },
      this.opciones,
    );
  }

  preguntas(organizacionId: string): Observable<PartidaDto[]> {
    return this.rest.request<void, PartidaDto[]>(
      { method: 'GET', url: `/api/app/cierre/preguntas/${organizacionId}` },
      this.opciones,
    );
  }

  resolver(organizacionId: string, partidaId: string, resolucion: ResolucionDePartida, nota: string | null): Observable<PartidaDto> {
    return this.rest.request<unknown, PartidaDto>(
      { method: 'POST', url: '/api/app/cierre/resolver', params: { organizacionId, partidaId }, body: { resolucion, nota } },
      this.opciones,
    );
  }

  responder(organizacionId: string, partidaId: string, respuesta: string): Observable<PartidaDto> {
    return this.rest.request<unknown, PartidaDto>(
      { method: 'POST', url: '/api/app/cierre/responder', params: { organizacionId, partidaId }, body: { respuesta } },
      this.opciones,
    );
  }

  cerrar(organizacionId: string, anio: number, mes: number): Observable<CierreDelMesDto> {
    return this.rest.request<void, CierreDelMesDto>(
      { method: 'POST', url: `/api/app/cierre/cerrar/${organizacionId}`, params: { anio, mes } },
      this.opciones,
    );
  }

  reabrir(organizacionId: string, anio: number, mes: number): Observable<CierreDelMesDto> {
    return this.rest.request<void, CierreDelMesDto>(
      { method: 'POST', url: `/api/app/cierre/reabrir/${organizacionId}`, params: { anio, mes } },
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
