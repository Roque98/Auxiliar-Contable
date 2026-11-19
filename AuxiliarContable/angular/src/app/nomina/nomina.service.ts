import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { RenglonDto } from '../mesa/renglones.component';

export interface IncidenciasDeNominaDto {
  faltas: number;
  incapacidad: number;
  horasJornada: number;
  horasExtraDobles: number;
  horasExtraTriples: number;
  diasDescansoTrabajados: number;
  festivosTrabajados: number;
  domingosTrabajados: number;
  aguinaldo: number;
  primaVacacional: number;
  otrasPercepciones: { concepto: string; importe: number; exento: number; integraSbc: boolean }[];
  otrasDeducciones: { concepto: string; importe: number; exento: number; integraSbc: boolean }[];
}

export interface TrabajadorDto {
  id?: string | null;
  nombre: string;
  rfc?: string | null;
  curp?: string | null;
  nss?: string | null;
  fechaIngreso: string;
  fechaBaja?: string | null;
  salarioDiario: number;
  salarioDiarioIntegrado?: number | null;
  diasAguinaldo: number;
  primaVacacionalPct: number;
  creditoInfonavitTipo?: string | null;
  creditoInfonavitValor?: number | null;
  fonacot?: number | null;
}

export interface NominaDeTrabajadorDto {
  id: string;
  nombre: string;
  dias: number;
  diasPagados: number;
  faltas: number;
  incapacidad: number;
  salarioMinimo: boolean;
  percepciones: { concepto: string; tipo: string; claveSat: string; importe: number; gravado: number; exento: number; integraSbc: boolean }[];
  deducciones: { concepto: string; tipo: string; claveSat: string; importe: number }[];
  totalPercepciones: number;
  gravado: number;
  exento: number;
  isr: number;
  subsidio: number;
  isrRetenido: number;
  factorIntegracion: number;
  sdi: number;
  sbc: number;
  cuotas: { ramo: string; base: number; patron: number; obrero: number; grupo: string }[];
  imssObrero: number;
  imssPatron: number;
  totalDeducciones: number;
  neto: number;
  renglones: RenglonDto[];
  avisos: string[];
  supuestos: string[];
}

export interface NominaDelMotorDto {
  trabajadores: NominaDeTrabajadorDto[];
  totales: {
    totalPercepciones: number; gravado: number; exento: number; isr: number; subsidio: number; isrRetenido: number;
    imssObrero: number; imssPatron: number; infonavitPatron: number; sarPatron: number; totalDeducciones: number; neto: number;
  };
  isn: { estado: string | null; tasa: number | null; importe: number; fundamento: string; verificado: boolean };
  poliza: { cuenta: string; debe: number; haber: number }[];
  avisos: string[];
}

export interface PeriodoDeNominaDto {
  id: string;
  inicio: string;
  fin: string;
  periodicidad: string;
  dias: number;
  calculadoUtc: string | null;
  incidencias: { trabajadorId: string; nombre: string | null; incidencias: IncidenciasDeNominaDto }[];
  resultado: NominaDelMotorDto | null;
  neto: number | null;
}

export interface NominaDelClienteDto {
  puedeEditar: boolean;
  faltaEnElPerfil: string[];
  trabajadores: TrabajadorDto[];
  periodos: PeriodoDeNominaDto[];
}

/** `NominaAppService` (`/api/app/nomina`): trabajadores y periodos de nómina de un cliente. Solo el despacho. */
@Injectable({ providedIn: 'root' })
export class NominaService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/nomina';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  delCliente(organizacionId: string, anio: number): Observable<NominaDelClienteDto> {
    return this.rest.request<void, NominaDelClienteDto>({ method: 'GET', url: `${this.base}/del-cliente/${organizacionId}`, params: { anio } }, this.opciones);
  }

  guardarTrabajador(organizacionId: string, t: TrabajadorDto): Observable<TrabajadorDto> {
    return this.rest.request<TrabajadorDto, TrabajadorDto>({ method: 'POST', url: `${this.base}/guardar-trabajador/${organizacionId}`, body: t }, this.opciones);
  }

  darDeBaja(organizacionId: string, trabajadorId: string, fecha: string | null): Observable<TrabajadorDto> {
    const params: Record<string, string> = { organizacionId, trabajadorId };
    if (fecha) {
      params['fecha'] = fecha;
    }
    return this.rest.request<void, TrabajadorDto>({ method: 'POST', url: `${this.base}/dar-de-baja`, params }, this.opciones);
  }

  crearPeriodo(organizacionId: string, inicio: string, fin: string, periodicidad: string): Observable<PeriodoDeNominaDto> {
    return this.rest.request<object, PeriodoDeNominaDto>(
      { method: 'POST', url: `${this.base}/crear-periodo/${organizacionId}`, body: { inicio, fin, periodicidad } }, this.opciones);
  }

  periodo(organizacionId: string, periodoId: string): Observable<PeriodoDeNominaDto> {
    return this.rest.request<void, PeriodoDeNominaDto>({ method: 'GET', url: `${this.base}/periodo`, params: { organizacionId, periodoId } }, this.opciones);
  }

  guardarIncidencias(organizacionId: string, periodoId: string, incidencias: PeriodoDeNominaDto['incidencias']): Observable<PeriodoDeNominaDto> {
    return this.rest.request<object, PeriodoDeNominaDto>(
      { method: 'POST', url: `${this.base}/guardar-incidencias`, params: { organizacionId, periodoId }, body: incidencias }, this.opciones);
  }

  calcular(organizacionId: string, periodoId: string): Observable<PeriodoDeNominaDto> {
    return this.rest.request<void, PeriodoDeNominaDto>({ method: 'POST', url: `${this.base}/calcular`, params: { organizacionId, periodoId } }, this.opciones);
  }

  borrarPeriodo(organizacionId: string, periodoId: string): Observable<void> {
    return this.rest.request<void, void>({ method: 'POST', url: `${this.base}/borrar-periodo`, params: { organizacionId, periodoId } }, this.opciones);
  }
}
