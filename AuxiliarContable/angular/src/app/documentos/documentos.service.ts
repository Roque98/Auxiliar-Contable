import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';
import { EstadoDeDocumento } from '../perfil-fiscal/perfil-fiscal.service';

/** Igual que `TipoDeDocumento` en el backend. */
export enum TipoDeDocumento {
  Otro = 0,
  ConstanciaDeSituacionFiscal = 1,
  Cfdi = 2,
  EstadoDeCuenta = 3,
  Ticket = 4,
}

/** Igual que `DireccionDelCfdi`. */
export enum DireccionDelCfdi {
  Desconocida = 0,
  Emitido = 1,
  Recibido = 2,
}

export interface CfdiResumenDto {
  uuid: string;
  tipoDeComprobante: string;
  direccion: DireccionDelCfdi;
  serie?: string | null;
  folio?: string | null;
  emisorRfc: string;
  emisorNombre?: string | null;
  receptorRfc: string;
  receptorNombre?: string | null;
  total: number;
  moneda: string;
  metodoDePago?: string | null;
  concepto?: string | null;
}

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
  cfdi?: CfdiResumenDto | null;
}

export interface ResultadoDeSubidaDto {
  documento: DocumentoDeBovedaDto;
  esDuplicado: boolean;
  aviso?: string | null;
}

export interface ResumenDelMesDto {
  anio: number;
  mes: number;
  tipo: TipoDeDocumento;
  cantidad: number;
  totalEmitido: number;
  totalRecibido: number;
}

export interface BuscarEnBovedaDto {
  organizacionId: string;
  anio?: number | null;
  mes?: number | null;
  tipo?: TipoDeDocumento | null;
  direccion?: DireccionDelCfdi | null;
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

  subir(organizacionId: string, tipo: TipoDeDocumento, archivo: File): Observable<ResultadoDeSubidaDto> {
    const datos = new FormData();
    datos.append('archivo', archivo, archivo.name);
    return this.rest.request<FormData, ResultadoDeSubidaDto>(
      { method: 'POST', url: `${this.base}/subir/${organizacionId}`, params: { tipo }, body: datos },
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
}

export const NOMBRES_DE_TIPO: Record<TipoDeDocumento, string> = {
  [TipoDeDocumento.Otro]: 'Otro',
  [TipoDeDocumento.ConstanciaDeSituacionFiscal]: 'Constancia fiscal',
  [TipoDeDocumento.Cfdi]: 'Factura (CFDI)',
  [TipoDeDocumento.EstadoDeCuenta]: 'Estado de cuenta',
  [TipoDeDocumento.Ticket]: 'Ticket',
};

export const MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
