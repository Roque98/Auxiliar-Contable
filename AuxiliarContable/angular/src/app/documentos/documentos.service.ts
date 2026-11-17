import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';
import { EstadoDeDocumento } from '../perfil-fiscal/perfil-fiscal.service';
import { TipoDeDocumento } from './tipos';

export { TipoDeDocumento };

export interface DocumentoDeBovedaDto {
  id: string;
  tipo: TipoDeDocumento;
  nombre: string;
  tipoMime: string;
  tamano: number;
  estado: EstadoDeDocumento;
  motivo?: string | null;
  subidoUtc: string;
  fecha: string;
}

export interface ResultadoDeSubidaDto {
  documento: DocumentoDeBovedaDto;
  esDuplicado: boolean;
  aviso?: string | null;
}

/** Una factura del mes ya clasificada (motor /cfdi/clasificar). */
export interface FacturaClasificadaDto {
  uuid: string;
  archivo: string | null;
  lado: 'emitida' | 'recibida';
  tipo: string | null;
  fecha: string | null;
  serieFolio: string | null;
  rfcContraparte: string | null;
  nombreContraparte: string | null;
  total: number | null;
  metodo: string | null;
  forma: string | null;
  uso: string | null;
  estadoDePago: 'por_definir' | 'parcial' | 'pagada' | null;
  pagado: number | null;
  categoria: string;
  conceptos: { clave: string | null; descripcion: string | null; importe: number | null; categoria: string | null; certeza: string | null; fundamento: string | null }[];
}

export interface FacturasClasificadasDto {
  actividad: { id: string; nombre: string } | null;
  facturas: FacturaClasificadaDto[];
  errores: { archivo: string; error: string }[];
}

export interface ResumenDelMesDto {
  anio: number;
  mes: number;
  tipo: TipoDeDocumento;
  cantidad: number;
}

export interface BuscarEnBovedaDto {
  organizacionId: string;
  anio?: number | null;
  mes?: number | null;
  tipo?: TipoDeDocumento | null;
  texto?: string | null;
  skipCount?: number;
  maxResultCount?: number;
}

/** `BovedaAppService` (`/api/app/boveda`). */
@Injectable({ providedIn: 'root' })
export class DocumentosService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/boveda';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  /** `anio`/`mes`: a qué mes corresponde; sin ellos, al de hoy. Una factura va siempre al de su XML. */
  subir(organizacionId: string, tipo: TipoDeDocumento, archivo: File, anio?: number, mes?: number): Observable<ResultadoDeSubidaDto> {
    const datos = new FormData();
    datos.append('archivo', archivo, archivo.name);
    return this.rest.request<FormData, ResultadoDeSubidaDto>(
      { method: 'POST', url: `${this.base}/subir/${organizacionId}`, params: anio && mes ? { tipo, anio, mes } : { tipo }, body: datos },
      this.opciones,
    );
  }

  cambiarMes(organizacionId: string, documentoId: string, anio: number, mes: number): Observable<DocumentoDeBovedaDto> {
    return this.rest.request<void, DocumentoDeBovedaDto>(
      { method: 'POST', url: `${this.base}/cambiar-mes`, params: { organizacionId, documentoId, anio, mes } },
      this.opciones,
    );
  }

  buscar(filtro: BuscarEnBovedaDto): Observable<{ totalCount: number; items: DocumentoDeBovedaDto[] }> {
    const params: Record<string, string | number> = {};
    for (const [k, v] of Object.entries(filtro)) {
      if (v !== null && v !== undefined && v !== '') {
        params[k] = v as string | number;
      }
    }
    return this.rest.request<void, { totalCount: number; items: DocumentoDeBovedaDto[] }>(
      { method: 'GET', url: this.base, params },
      this.opciones,
    );
  }

  resumen(organizacionId: string): Observable<ResumenDelMesDto[]> {
    return this.rest.request<void, ResumenDelMesDto[]>(
      { method: 'GET', url: `${this.base}/resumen/${organizacionId}` },
      this.opciones,
    );
  }

  descargar(organizacionId: string, documentoId: string): Observable<Blob> {
    return this.rest.request<void, Blob>(
      {
        method: 'GET',
        url: `${this.base}/archivo`,
        params: { organizacionId, documentoId },
        responseType: 'blob',
      },
      this.opciones,
    );
  }

  eliminar(organizacionId: string, documentoId: string): Observable<void> {
    return this.rest.request<void, void>(
      { method: 'POST', url: `${this.base}/eliminar`, params: { organizacionId, documentoId } },
      this.opciones,
    );
  }

  /** «Mis facturas»: las del mes clasificadas por el motor (`BancosAppService.ClasificarFacturas`). */
  clasificarFacturas(organizacionId: string, anio: number, mes: number): Observable<FacturasClasificadasDto> {
    return this.rest.request<void, FacturasClasificadasDto>(
      { method: 'POST', url: `/api/app/bancos/clasificar-facturas/${organizacionId}`, params: { anio, mes } },
      this.opciones,
    );
  }
}

export const NOMBRES_DE_TIPO: Record<TipoDeDocumento, string> = {
  [TipoDeDocumento.Otro]: 'Otro',
  [TipoDeDocumento.ConstanciaDeSituacionFiscal]: 'Constancia fiscal',
  [TipoDeDocumento.Cfdi]: 'Factura (CFDI)',
  [TipoDeDocumento.EstadoDeCuenta]: 'Estado de cuenta',
  [TipoDeDocumento.Ticket]: 'Ticket',
};

export const MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
