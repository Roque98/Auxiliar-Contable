import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

/** Igual que `OrigenDelDato` en el backend. */
export enum OrigenDelDato {
  ExtraidoDeCsf = 1,
  RespondidoPorElCliente = 2,
  Inferido = 3,
  CapturadoPorElDespacho = 4,
}

/** Igual que `TipoDeCampo`. */
export enum TipoDeCampo {
  Texto = 0,
  Fecha = 1,
  SiNo = 2,
  Lista = 3,
}

/** Igual que `GrupoDeCampos`. */
export enum GrupoDeCampos {
  Identificacion = 0,
  Fiscal = 1,
  Operacion = 2,
}

/** Igual que `EstadoDeDocumento`. */
export enum EstadoDeDocumento {
  Recibido = 0,
  Procesado = 1,
  ConError = 2,
}

export interface ClienteVisibleDto {
  id: string;
  nombre: string;
  puedeEditar: boolean;
}

export interface DatoDelPerfilDto {
  clave: string;
  etiqueta: string;
  grupo: GrupoDeCampos;
  tipo: TipoDeCampo;
  vieneEnLaCsf: boolean;
  valor?: string | null;
  origen?: OrigenDelDato | null;
  confirmado: boolean;
  documentoId?: string | null;
  actualizadoUtc?: string | null;
}

export interface CsfDelPerfilDto {
  documentoId: string;
  nombre: string;
  subidaUtc: string;
  estado: EstadoDeDocumento;
  motivo?: string | null;
  avisos: string[];
}

export interface PerfilFiscalDto {
  organizacionId: string;
  puedeEditar: boolean;
  campos: DatoDelPerfilDto[];
  csf?: CsfDelPerfilDto | null;
}

/** `PerfilFiscalAppService` (`/api/app/perfil-fiscal`). */
@Injectable({ providedIn: 'root' })
export class PerfilFiscalService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/perfil-fiscal';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  clientes(): Observable<ClienteVisibleDto[]> {
    return this.rest.request<void, ClienteVisibleDto[]>({ method: 'GET', url: `${this.base}/clientes` }, this.opciones);
  }

  obtener(organizacionId: string): Observable<PerfilFiscalDto> {
    return this.rest.request<void, PerfilFiscalDto>({ method: 'GET', url: this.base, params: { organizacionId } }, this.opciones);
  }

  subirCsf(organizacionId: string, archivo: File): Observable<PerfilFiscalDto> {
    const datos = new FormData();
    datos.append('archivo', archivo, archivo.name);
    return this.rest.request<FormData, PerfilFiscalDto>(
      { method: 'POST', url: `${this.base}/subir-csf/${organizacionId}`, body: datos },
      this.opciones,
    );
  }

  confirmar(organizacionId: string, campo: string): Observable<PerfilFiscalDto> {
    return this.rest.request<void, PerfilFiscalDto>(
      { method: 'POST', url: `${this.base}/confirmar/${organizacionId}`, params: { campo } },
      this.opciones,
    );
  }

  confirmarConstancia(organizacionId: string): Observable<PerfilFiscalDto> {
    return this.rest.request<void, PerfilFiscalDto>(
      { method: 'POST', url: `${this.base}/confirmar-constancia/${organizacionId}` },
      this.opciones,
    );
  }

  responder(organizacionId: string, campo: string, valor: string): Observable<PerfilFiscalDto> {
    return this.rest.request<{ campo: string; valor: string }, PerfilFiscalDto>(
      { method: 'POST', url: `${this.base}/responder/${organizacionId}`, body: { campo, valor } },
      this.opciones,
    );
  }
}

const ORIGENES: Record<OrigenDelDato, string> = {
  [OrigenDelDato.ExtraidoDeCsf]: 'De tu constancia',
  [OrigenDelDato.RespondidoPorElCliente]: 'Lo dijiste tú',
  [OrigenDelDato.Inferido]: 'Sugerido',
  [OrigenDelDato.CapturadoPorElDespacho]: 'Del despacho',
};

export function etiquetaDeOrigen(origen: OrigenDelDato | null | undefined): string {
  return origen ? ORIGENES[origen] : '';
}

/** Un valor del perfil listo para leer: fechas en dd/mm/aaaa, «si»/«no» con acento, listas en renglones. */
export function valorLegible(dato: DatoDelPerfilDto): string[] {
  const v = dato.valor;
  if (!v) {
    return [];
  }
  if (dato.clave === 'tipo_persona') {
    return [v === 'moral' ? 'Persona moral' : v === 'fisica' ? 'Persona física' : v];
  }
  switch (dato.tipo) {
    case TipoDeCampo.Fecha: {
      const [a, m, d] = v.split('-');
      return [d && m && a ? `${d}/${m}/${a}` : v];
    }
    case TipoDeCampo.SiNo:
      return [v === 'si' ? 'Sí' : 'No'];
    case TipoDeCampo.Lista:
      try {
        const lista = JSON.parse(v) as unknown[];
        return lista.map(renglonDeLista);
      } catch {
        return [v];
      }
    default:
      return [v];
  }
}

function renglonDeLista(item: unknown): string {
  if (typeof item === 'string') {
    return item;
  }
  const o = item as { clave?: string; descripcion?: string; porcentaje?: number | null; fechaInicio?: string | null };
  const partes = [o.clave ? `${o.clave} · ${o.descripcion ?? ''}` : (o.descripcion ?? '')];
  if (o.porcentaje != null) {
    partes.push(`${o.porcentaje} %`);
  }
  if (o.fechaInicio) {
    const [a, m, d] = o.fechaInicio.split('-');
    partes.push(`desde ${d}/${m}/${a}`);
  }
  return partes.join(' — ');
}
