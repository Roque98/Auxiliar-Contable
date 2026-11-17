import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export enum EstadoDeSolicitud {
  Pendiente = 0,
  Emitida = 1,
  Rechazada = 2,
  Cancelada = 3,
}

export interface ReceptorDto {
  rfc: string;
  nombre: string;
  codigoPostal: string;
  regimen: string;
}

export interface ConceptoDto {
  claveProdServ: string;
  descripcion: string;
  cantidad: number;
  claveUnidad: string;
  precioUnitario: number;
  tasaIva: number | null;
  importe?: number;
  iva?: number;
}

export interface SolicitudDto {
  id: string;
  organizacionId: string;
  cliente: string | null;
  estado: EstadoDeSolicitud;
  pedidaUtc: string;
  receptor: ReceptorDto;
  usoCfdi: string;
  formaDePago: string;
  metodoDePago: string;
  conceptos: ConceptoDto[];
  subtotal: number;
  iva: number;
  total: number;
  nota: string | null;
  respuesta: string | null;
  atendidaUtc: string | null;
  nombreDelXml: string | null;
  nombreDelPdf: string | null;
}

export interface SolicitudesDelClienteDto {
  solicitudes: SolicitudDto[];
  receptores: ReceptorDto[];
  puedePedir: boolean;
  esEquipo: boolean;
}

export interface CrearSolicitudDto {
  receptor: ReceptorDto;
  usoCfdi: string;
  formaDePago: string;
  metodoDePago: string;
  conceptos: ConceptoDto[];
  nota: string | null;
}

/** `SolicitudesDeFacturaAppService` (`/api/app/solicitudes-de-factura`). */
@Injectable({ providedIn: 'root' })
export class FacturarService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/solicitudes-de-factura';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  delCliente(organizacionId: string): Observable<SolicitudesDelClienteDto> {
    return this.rest.request<void, SolicitudesDelClienteDto>({ method: 'GET', url: this.base, params: { organizacionId } }, this.opciones);
  }

  pendientes(): Observable<SolicitudDto[]> {
    return this.rest.request<void, SolicitudDto[]>({ method: 'GET', url: `${this.base}/pendientes` }, this.opciones);
  }

  crear(organizacionId: string, s: CrearSolicitudDto): Observable<SolicitudDto> {
    return this.rest.request<CrearSolicitudDto, SolicitudDto>({ method: 'POST', url: `${this.base}/crear/${organizacionId}`, body: s }, this.opciones);
  }

  cancelar(organizacionId: string, solicitudId: string): Observable<SolicitudDto> {
    return this.rest.request<void, SolicitudDto>(
      { method: 'POST', url: `${this.base}/cancelar`, params: { organizacionId, solicitudId } }, this.opciones);
  }

  atender(organizacionId: string, solicitudId: string, xmlDocumentoId: string, pdfDocumentoId: string | null, respuesta: string | null) {
    return this.rest.request<unknown, SolicitudDto>(
      { method: 'POST', url: `${this.base}/atender`, params: { organizacionId, solicitudId }, body: { xmlDocumentoId, pdfDocumentoId, respuesta } },
      this.opciones);
  }

  rechazar(organizacionId: string, solicitudId: string, respuesta: string) {
    return this.rest.request<unknown, SolicitudDto>(
      { method: 'POST', url: `${this.base}/rechazar`, params: { organizacionId, solicitudId }, body: { respuesta } }, this.opciones);
  }

  leerConstancia(organizacionId: string, pdfBase64: string): Observable<ReceptorDto> {
    return this.rest.request<unknown, ReceptorDto>(
      { method: 'POST', url: `${this.base}/leer-constancia-del-receptor/${organizacionId}`, body: { pdfBase64 } }, this.opciones);
  }
}

// Catálogos del SAT (CFDI 4.0) que más usa un despacho de personas físicas y pymes.
export const USOS_CFDI: [string, string][] = [
  ['G01', 'Adquisición de mercancías'], ['G02', 'Devoluciones, descuentos o bonificaciones'], ['G03', 'Gastos en general'],
  ['I01', 'Construcciones'], ['I02', 'Mobiliario y equipo de oficina'], ['I03', 'Equipo de transporte'], ['I04', 'Equipo de cómputo'],
  ['I05', 'Dados, troqueles, moldes'], ['I06', 'Comunicaciones telefónicas'], ['I07', 'Comunicaciones satelitales'], ['I08', 'Otra maquinaria y equipo'],
  ['D01', 'Honorarios médicos, dentales y hospitalarios'], ['D02', 'Gastos médicos por incapacidad'], ['D03', 'Gastos funerales'], ['D04', 'Donativos'],
  ['D05', 'Intereses por créditos hipotecarios'], ['D06', 'Aportaciones voluntarias al SAR'], ['D07', 'Primas de seguros de gastos médicos'],
  ['D08', 'Transportación escolar obligatoria'], ['D09', 'Depósitos para el ahorro, planes de pensiones'], ['D10', 'Colegiaturas'],
  ['S01', 'Sin efectos fiscales'], ['CP01', 'Pagos'], ['CN01', 'Nómina'],
];

export const FORMAS_DE_PAGO: [string, string][] = [
  ['01', 'Efectivo'], ['02', 'Cheque nominativo'], ['03', 'Transferencia electrónica'], ['04', 'Tarjeta de crédito'],
  ['05', 'Monedero electrónico'], ['06', 'Dinero electrónico'], ['08', 'Vales de despensa'], ['28', 'Tarjeta de débito'],
  ['29', 'Tarjeta de servicios'], ['30', 'Aplicación de anticipos'], ['99', 'Por definir'],
];

export const REGIMENES: [string, string][] = [
  ['601', 'General de Ley Personas Morales'], ['603', 'Personas Morales con Fines no Lucrativos'], ['605', 'Sueldos y Salarios'],
  ['606', 'Arrendamiento'], ['608', 'Demás ingresos'], ['612', 'Personas Físicas con Actividades Empresariales y Profesionales'],
  ['616', 'Sin obligaciones fiscales'], ['621', 'Incorporación Fiscal'], ['625', 'Plataformas Tecnológicas'],
  ['626', 'Régimen Simplificado de Confianza'],
];

export const UNIDADES: [string, string][] = [
  ['H87', 'Pieza'], ['E48', 'Unidad de servicio'], ['ACT', 'Actividad'], ['KGM', 'Kilogramo'], ['LTR', 'Litro'], ['MTR', 'Metro'],
  ['MTK', 'Metro cuadrado'], ['XBX', 'Caja'], ['XPK', 'Paquete'], ['HUR', 'Hora'], ['DAY', 'Día'], ['MON', 'Mes'], ['SET', 'Conjunto'],
];

export const TASAS: { valor: number | null; nombre: string }[] = [
  { valor: 0.16, nombre: '16 %' }, { valor: 0.08, nombre: '8 % (frontera)' }, { valor: 0, nombre: '0 %' }, { valor: null, nombre: 'Exento' },
];
