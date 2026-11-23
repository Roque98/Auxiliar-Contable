import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface LeyDelClienteDto {
  id: string;
  documentoId: string;
  nombreDelArchivo: string;
  ambito: 'estatal' | 'municipal';
  estado?: string | null;
  municipio?: string | null;
  materia?: string | null;
  titulo?: string | null;
  lectura: 'Pendiente' | 'Leyendo' | 'Lista' | 'Error';
  paginas?: number | null;
  paginasConOcr?: number | null;
  paginasLeidas?: number | null;
  articulos?: number | null;
  analisis: 'sin_leer' | 'por_analizar' | 'analizada' | 'error';
  resumen?: string | null;
  noEncontrado: string[];
  temas: string[];
  analizadoUtc?: string | null;
  error?: string | null;
  creationTime: string;
  /** «MOR/salud» si es una ley que ya teníamos del estado (no la subió el cliente). */
  biblioteca?: string | null;
}

export interface HallazgoDeLeyDto {
  id: string;
  leyId: string;
  tipo: 'valor' | 'obligacion' | 'beneficio' | 'aviso';
  tema?: string | null;
  titulo: string;
  clave?: string | null;
  valor?: string | null;
  valorNumerico?: number | null;
  valorDelSistema?: string | null;
  condicion?: string | null;
  periodicidad?: string | null;
  fechaLimite?: string | null;
  articulo: string;
  etiqueta?: string | null;
  pagina?: number | null;
  cita: string;
  explicacion?: string | null;
  decision: 'Propuesto' | 'Aceptado' | 'Rechazado' | 'Reemplazado';
  decididoUtc?: string | null;
  ley?: string | null;
}

export interface PreguntaSobreLeyesDto {
  id: string;
  pregunta: string;
  estado: 'Pendiente' | 'Lista' | 'Error';
  respuesta?: string | null;
  error?: string | null;
  creationTime: string;
}

export interface MisLeyesDto {
  mensaje: string;
  esDelDespacho: boolean;
  estadoDelCliente?: string | null;
  leyes: LeyDelClienteDto[];
  hallazgos: HallazgoDeLeyDto[];
  preguntas: PreguntaSobreLeyesDto[];
  enCurso: boolean;
}

export interface LeyQueTeAplicaDto {
  id: string;
  nombre: string;
  nombreOficial?: string | null;
  bloque: string;
  ambito: 'estatal' | 'municipal';
  clasificacion?: 'operar' | 'inmueble' | 'actividad' | null;
  motivos: string[];
  pide?: string | null;
  estatus: 'local' | 'pendiente' | 'no_existe' | 'federal' | 'estatal' | 'municipal' | 'sin_ficha';
  ultimaReforma?: string | null;
  url?: string | null;
  nota?: string | null;
  disponible: boolean;
  anual: boolean;
  enMisLeyes?: string | null;
}

export interface LeyesQueTeAplicanDto {
  estado?: string | null;
  nombreDelEstado?: string | null;
  municipio?: string | null;
  historicoMunicipal?: string | null;
  aviso?: string | null;
  historico?: string | null;
  fuentes: { url: string; que: string }[];
  disparadores: string[];
  bloques: Record<string, string>;
  leyes: LeyQueTeAplicaDto[];
}

/** La respuesta del motor a una pregunta (JSON guardado en `respuesta`). */
export interface RespuestaSobreLeyes {
  encontrado: boolean;
  respuesta: string;
  falta?: string;
  citas: { articulo: string; etiqueta?: string; pagina?: number; cita: string; nombre_documento?: string }[];
}

export function leerRespuesta(json?: string | null): RespuestaSobreLeyes | null {
  if (!json) {
    return null;
  }
  try {
    const r = JSON.parse(json) as Partial<RespuestaSobreLeyes>;
    return { encontrado: !!r.encontrado, respuesta: r.respuesta ?? '', falta: r.falta, citas: Array.isArray(r.citas) ? r.citas : [] };
  } catch {
    return null;
  }
}

export const ESTADOS: { clave: string; nombre: string }[] = [
  { clave: 'AGS', nombre: 'Aguascalientes' }, { clave: 'BC', nombre: 'Baja California' }, { clave: 'BCS', nombre: 'Baja California Sur' },
  { clave: 'CAMP', nombre: 'Campeche' }, { clave: 'CHIS', nombre: 'Chiapas' }, { clave: 'CHIH', nombre: 'Chihuahua' },
  { clave: 'CDMX', nombre: 'Ciudad de México' }, { clave: 'COAH', nombre: 'Coahuila' }, { clave: 'COL', nombre: 'Colima' },
  { clave: 'DGO', nombre: 'Durango' }, { clave: 'MEX', nombre: 'Estado de México' }, { clave: 'GTO', nombre: 'Guanajuato' },
  { clave: 'GRO', nombre: 'Guerrero' }, { clave: 'HGO', nombre: 'Hidalgo' }, { clave: 'JAL', nombre: 'Jalisco' },
  { clave: 'MICH', nombre: 'Michoacán' }, { clave: 'MOR', nombre: 'Morelos' }, { clave: 'NAY', nombre: 'Nayarit' },
  { clave: 'NL', nombre: 'Nuevo León' }, { clave: 'OAX', nombre: 'Oaxaca' }, { clave: 'PUE', nombre: 'Puebla' },
  { clave: 'QRO', nombre: 'Querétaro' }, { clave: 'QROO', nombre: 'Quintana Roo' }, { clave: 'SLP', nombre: 'San Luis Potosí' },
  { clave: 'SIN', nombre: 'Sinaloa' }, { clave: 'SON', nombre: 'Sonora' }, { clave: 'TAB', nombre: 'Tabasco' },
  { clave: 'TAMPS', nombre: 'Tamaulipas' }, { clave: 'TLAX', nombre: 'Tlaxcala' }, { clave: 'VER', nombre: 'Veracruz' },
  { clave: 'YUC', nombre: 'Yucatán' }, { clave: 'ZAC', nombre: 'Zacatecas' },
];

/** Qué valor del sistema reemplaza cada clave, en palabras. */
export const QUE_REEMPLAZA: Record<string, string> = {
  'isn.tasa': 'Tasa del impuesto sobre nómina',
  'isn.dia': 'Día límite del impuesto sobre nómina',
  'recargos.tasa': 'Tasa de recargos estatal',
};

@Injectable({ providedIn: 'root' })
export class MisLeyesService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/leyes-del-cliente';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  misLeyes(organizacionId: string): Observable<MisLeyesDto> {
    return this.rest.request<void, MisLeyesDto>({ method: 'GET', url: `${this.base}/mis-leyes/${organizacionId}` }, this.opciones);
  }

  subir(organizacionId: string, archivo: File, ambito: string, estado: string | null, municipio: string | null, materia: string | null):
    Observable<LeyDelClienteDto> {
    const datos = new FormData();
    datos.append('archivo', archivo, archivo.name);
    const params: Record<string, string> = { ambito };
    if (estado) { params['estado'] = estado; }
    if (municipio) { params['municipio'] = municipio; }
    if (materia) { params['materia'] = materia; }
    return this.rest.request<FormData, LeyDelClienteDto>(
      { method: 'POST', url: `${this.base}/subir/${organizacionId}`, params, body: datos }, this.opciones);
  }

  eliminar(organizacionId: string, id: string): Observable<void> {
    // ABP: EliminarAsync no empieza con Delete, así que es POST /{id}/eliminar/{organizacionId}
    return this.rest.request<void, void>({ method: 'POST', url: `${this.base}/${id}/eliminar/${organizacionId}` }, this.opciones);
  }

  reanalizar(organizacionId: string, id: string): Observable<void> {
    return this.rest.request<void, void>({ method: 'POST', url: `${this.base}/${id}/reanalizar/${organizacionId}` }, this.opciones);
  }

  decidir(organizacionId: string, id: string, aceptar: boolean): Observable<HallazgoDeLeyDto> {
    return this.rest.request<{ aceptar: boolean }, HallazgoDeLeyDto>(
      { method: 'POST', url: `${this.base}/${id}/decidir/${organizacionId}`, body: { aceptar } }, this.opciones);
  }

  queTeAplican(organizacionId: string): Observable<LeyesQueTeAplicanDto> {
    return this.rest.request<void, LeyesQueTeAplicanDto>({ method: 'GET', url: `${this.base}/que-te-aplican/${organizacionId}` }, this.opciones);
  }

  usarDeLaBiblioteca(organizacionId: string, ley: string): Observable<LeyDelClienteDto> {
    return this.rest.request<void, LeyDelClienteDto>(
      { method: 'POST', url: `${this.base}/usar-de-la-biblioteca/${organizacionId}`, params: { ley } }, this.opciones);
  }

  preguntar(organizacionId: string, pregunta: string): Observable<PreguntaSobreLeyesDto> {
    return this.rest.request<{ pregunta: string }, PreguntaSobreLeyesDto>(
      { method: 'POST', url: `${this.base}/preguntar/${organizacionId}`, body: { pregunta } }, this.opciones);
  }
}
