import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface EstudioDeSentencia {
  conclusion: string;
  /** contribuyente, autoridad, mixto o procesal. */
  aFavor: string;
  reglaPractica: string;
  aplicaCuando: string;
  queDocumentar: string[];
  fundamentos: string[];
  temas: string[];
  vigencia: string;
  relevancia: string;
}

export interface SentenciaDto {
  id: string;
  fuente: string;
  clave: string | null;
  tipo: string | null;
  instancia: string | null;
  materia: string | null;
  rubro: string;
  publicacion: string | null;
  fecha: string | null;
  url: string | null;
  texto: string;
  estudio: EstudioDeSentencia | null;
}

export interface PaginaDeSentencias {
  resultados: SentenciaDto[];
  /** Hay más tesis sobre el tema: «Ver más» pide la página siguiente. */
  hayMas: boolean;
}

export interface EstadoDeLaLibreria {
  estado: {
    libreria: {
      tesis: number;
      porFuente: Record<string, number>;
      estudiadas: number;
      ultimaPublicacion: string | null;
      ultimaActualizacion: string | null;
    };
    trabajo: {
      corriendo: boolean;
      fase: string | null;
      inicio: string | null;
      fin: string | null;
      error: string | null;
      bitacora: string[];
    };
  };
  puedeActualizar: boolean;
}

/** `SentenciasAppService` (`/api/app/sentencias`): la librería de tesis del motor fiscal. */
@Injectable({ providedIn: 'root' })
export class SentenciasService {
  private readonly rest = inject(RestService);
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  buscar(consulta: string, fuente: string | null, pagina = 1): Observable<PaginaDeSentencias> {
    return this.rest.request<{ consulta: string; fuente: string | null; pagina: number }, PaginaDeSentencias>(
      { method: 'POST', url: '/api/app/sentencias/buscar', body: { consulta, fuente, pagina } },
      this.opciones,
    );
  }

  estado(): Observable<EstadoDeLaLibreria> {
    return this.rest.request<void, EstadoDeLaLibreria>({ method: 'GET', url: '/api/app/sentencias/estado' }, this.opciones);
  }

  actualizar(): Observable<EstadoDeLaLibreria> {
    return this.rest.request<void, EstadoDeLaLibreria>({ method: 'POST', url: '/api/app/sentencias/actualizar' }, this.opciones);
  }
}

/** Cómo se dice en pantalla a quién favoreció el criterio. */
export const A_FAVOR: Record<string, { texto: string; clase: string }> = {
  contribuyente: { texto: 'A favor del contribuyente', clase: 'ig-etiqueta-exito' },
  autoridad: { texto: 'A favor de la autoridad', clase: 'ig-etiqueta-error' },
  mixto: { texto: 'Depende de lo que se pruebe', clase: 'ig-etiqueta-alerta' },
  procesal: { texto: 'Regla del juicio', clase: 'ig-etiqueta-tinta' },
};
