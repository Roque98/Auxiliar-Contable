import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface ArticuloDeLeyDto {
  ley: string;
  ordenamiento: string;
  articulo: string | null;
  ubicacion: string | null;
  ultimaReformaDof: string | null;
  notaDeVigencia: string | null;
  url: string | null;
  texto: string;
}

/** `LeyesAppService` (`/api/app/leyes`): el índice de leyes del motor fiscal, sin IA. */
@Injectable({ providedIn: 'root' })
export class LeyesService {
  private readonly rest = inject(RestService);
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  buscar(consulta: string, ley: string | null): Observable<ArticuloDeLeyDto[]> {
    return this.rest.request<{ consulta: string; ley: string | null }, ArticuloDeLeyDto[]>(
      { method: 'POST', url: '/api/app/leyes/buscar', body: { consulta, ley } },
      this.opciones,
    );
  }
}

/** Las que más se consultan en un despacho; las siglas son las del índice del motor. */
export const LEYES: { clave: string; nombre: string }[] = [
  { clave: 'LISR', nombre: 'Impuesto sobre la Renta' },
  { clave: 'LIVA', nombre: 'Impuesto al Valor Agregado' },
  { clave: 'CFF', nombre: 'Código Fiscal de la Federación' },
  { clave: 'RMF', nombre: 'Resolución Miscelánea Fiscal 2026' },
  { clave: 'LIEPS', nombre: 'IEPS' },
  { clave: 'LFT', nombre: 'Ley Federal del Trabajo' },
  { clave: 'LSS', nombre: 'Ley del Seguro Social' },
  { clave: 'NIF', nombre: 'Normas de Información Financiera 2026' },
  { clave: 'LFD', nombre: 'Ley Federal de Derechos' },
  { clave: 'LAdua', nombre: 'Ley Aduanera' },
  { clave: 'LGSM', nombre: 'Ley General de Sociedades Mercantiles' },
  { clave: 'CPEUM', nombre: 'Constitución' },
];
