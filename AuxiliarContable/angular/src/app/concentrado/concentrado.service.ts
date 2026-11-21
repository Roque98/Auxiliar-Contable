import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface RenglonDelPapel {
  concepto: string;
  importe?: number | null;
  fundamento: string;
  /** renglon, subtotal, resultado, tasa (en %), factor o nota. */
  tipo: string;
  /** ingresos, deducciones, iva_cobrado, iva_acreditable, retenciones, retenciones_a_terceros_isr/iva (null: no se baja). */
  detalle?: string | null;
}

export interface AdeudoAnteriorDto {
  mes: number;
  periodo: string;
  impuesto: number;
  pagado: number;
  limite: string;
  mesesDeAtraso: number;
  factor: number;
  actualizacion: number;
  recargos: number;
  total: number;
  fundamento: string;
}

export interface PagoDeObligacionDto {
  id: string;
  anio: number;
  mes: number;
  clave: string;
  importe: number;
  fechaDePago: string;
  nota?: string | null;
}

export interface ObligacionFiscalDto {
  clave: string;
  nombre: string;
  nivel: 'federal' | 'estatal';
  descripcion: string;
  periodo: string;
  limite?: string | null;
  limiteAproximado: boolean;
  impuesto?: number | null;
  aFavor: number;
  pagado: number;
  adeudos: AdeudoAnteriorDto[];
  actualizacion: number;
  recargos: number;
  total: number;
  /** por_pagar, vencido, pagado, a_favor, sin_impuesto, sin_calcular o en_curso. */
  estado: string;
  renglones: RenglonDelPapel[];
  fundamento: string;
  nota?: string | null;
  pagos: PagoDeObligacionDto[];
}

export interface DeudasFiscalesDto {
  anio: number;
  mes: number;
  calculado: boolean;
  calculadoUtc?: string | null;
  actualizando: boolean;
  error?: string | null;
  puedeRegistrarPagos: boolean;
  regimen?: string | null;
  claseDelCalculo?: string | null;
  totalAPagar: number;
  obligaciones: ObligacionFiscalDto[];
  noAplican: string[];
  avisos: string[];
  fuente: { facturas: number; canceladas: number; ultimaFacturaUtc?: string | null; periodosDeNomina: number; supuestos: string[] };
}

export interface FacturaResumenDto {
  uuid: string;
  fecha: string;
  rfc: string;
  nombre: string;
  concepto: string;
  total: number;
  importe: number;
  estado: string;
  motivo?: string | null;
}

export interface GrupoDeFacturasDto {
  clave: string;
  titulo: string;
  descripcion: string;
  importe: number;
  facturas: FacturaResumenDto[];
}

export interface DetalleDeRenglonDto {
  detalle: string;
  titulo: string;
  explicacion: string;
  soloDelMes: boolean;
  grupos: GrupoDeFacturasDto[];
}

export interface MotivoDelCalculo {
  regla: string;
  /** requisito, ley, inversion, indispensable, revisar o personal. */
  clase: string;
  hipotesis: string;
  articulo: string;
  resultado: string;
  concepto: string;
  importe?: number | null;
  noDeducible?: number | null;
  solucion: string;
  pregunta: string;
  explicacion?: string | null;
}

export interface FacturaDelCalculo {
  uuid: string;
  fecha: string;
  rfc: string;
  nombre: string;
  tipo: string;
  metodo: string;
  forma: string;
  uso: string;
  total: number;
  base: number;
  iva: number;
  acumulable: number;
  deducible: number;
  ivaAcreditable: number;
  retIsr: number;
  retIva: number;
  estado: string;
  motivos: MotivoDelCalculo[];
  meses: string[];
  concepto: string;
}

export interface FacturaExplicadaDto {
  factura: FacturaDelCalculo;
  lado: 'ingreso' | 'deduccion';
  resumen: string;
  regimen?: string | null;
  origen?: { documentoId: string; archivo: string; entroUtc: string; quien: string; estatusSat?: string | null; estatusSatUtc?: string | null } | null;
}

export interface RegistrarPagoDto {
  anio: number;
  mes: number;
  clave: string;
  importe: number;
  fechaDePago: string;
  nota?: string | null;
}

export interface SaldoDeCuentaDto {
  clave: string;
  nombre: string;
  saldo: number;
}

export interface RubroContableDto {
  clave: string;
  nombre: string;
  grupo: string;
  nif: string;
  saldo: number;
  virtual: boolean;
  cuentas: SaldoDeCuentaDto[];
}

export interface RubrosContablesDto {
  seccion: string;
  titulo: string;
  explicacion: string;
  anio: number;
  mes: number;
  total: number;
  rubros: RubroContableDto[];
  avisos: string[];
  hayContabilidad: boolean;
  desde: string;
  mesesConPolizas: number;
  ultimaContabilizacionUtc?: string | null;
}

export interface MovimientoDeCuentaDto {
  fecha: string;
  poliza: string;
  concepto: string;
  cuenta: string;
  texto: string;
  rfc?: string | null;
  uuid?: string | null;
  debe: number;
  haber: number;
  archivo?: string | null;
  entroUtc?: string | null;
}

export interface CuentaContableDto {
  clave: string;
  nombre: string;
  nif: string;
  naturaleza: string;
  saldoInicial: number;
  cargos: number;
  abonos: number;
  saldoFinal: number;
  subcuentas: SaldoDeCuentaDto[];
  terceros: { rfc: string; nombre: string; saldo: number; movimientos: number }[];
  movimientos: MovimientoDeCuentaDto[];
  movimientosRecortados: boolean;
}

/** La hoja concentrado del cliente: deudas fiscales y pestañas contables. */
@Injectable({ providedIn: 'root' })
export class ConcentradoService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/concentrado';
  private readonly api = { apiName: 'Default' };

  deudasFiscales(organizacionId: string, anio: number, mes: number): Observable<DeudasFiscalesDto> {
    return this.rest.request<void, DeudasFiscalesDto>({ method: 'GET', url: `${this.base}/deudas-fiscales/${organizacionId}`, params: { anio, mes } }, this.api);
  }

  detalle(organizacionId: string, anio: number, mes: number, detalle: string, soloDelMes: boolean): Observable<DetalleDeRenglonDto> {
    return this.rest.request<void, DetalleDeRenglonDto>({ method: 'GET', url: `${this.base}/detalle/${organizacionId}`, params: { anio, mes, detalle, soloDelMes } }, this.api);
  }

  factura(organizacionId: string, anio: number, mes: number, uuid: string): Observable<FacturaExplicadaDto> {
    return this.rest.request<void, FacturaExplicadaDto>({ method: 'GET', url: `${this.base}/factura/${organizacionId}`, params: { anio, mes, uuid } }, this.api);
  }

  rubros(organizacionId: string, anio: number, mes: number, seccion: string): Observable<RubrosContablesDto> {
    return this.rest.request<void, RubrosContablesDto>({ method: 'GET', url: `${this.base}/rubros/${organizacionId}`, params: { anio, mes, seccion } }, this.api);
  }

  cuenta(organizacionId: string, anio: number, mes: number, cuenta: string): Observable<CuentaContableDto> {
    return this.rest.request<void, CuentaContableDto>({ method: 'GET', url: `${this.base}/cuenta/${organizacionId}`, params: { anio, mes, cuenta } }, this.api);
  }

  registrarPago(organizacionId: string, dto: RegistrarPagoDto): Observable<PagoDeObligacionDto> {
    return this.rest.request<RegistrarPagoDto, PagoDeObligacionDto>({ method: 'POST', url: `${this.base}/registrar-pago/${organizacionId}`, body: dto }, this.api);
  }

  borrarPago(organizacionId: string, pagoId: string): Observable<void> {
    return this.rest.request<void, void>({ method: 'POST', url: `${this.base}/borrar-pago`, params: { organizacionId, pagoId } }, this.api);
  }
}
