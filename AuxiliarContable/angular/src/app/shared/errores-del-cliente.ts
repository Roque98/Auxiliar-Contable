import { EsFalloDeCarga } from './recarga-tras-despliegue';

/** Lo que recibe `ErroresDelClienteAppService` (API): un error de JavaScript que vio un usuario. */
export interface ErrorDelCliente {
  origen: 'web';
  mensaje: string;
  pila?: string;
  ruta?: string;
  version?: string;
  agente?: string;
}

/** Tope de reportes por carga de página: un error en bucle no inunda los logs (la API también limita). */
export const MAXIMO_POR_PAGINA = 15;

/**
 * Qué se manda de un error, o null si no vale la pena: los fallos de carga tras un despliegue (los
 * arregla `RecargaTrasDespliegue` recargando) y las peticiones sin red (status 0: el usuario se
 * quedó sin internet, no es un fallo nuestro). Puro: se prueba sin Angular.
 */
export function armarReporte(error: unknown, ruta: string, version: string, agente: string): ErrorDelCliente | null {
  if (EsFalloDeCarga(error)) {
    return null;
  }

  const e = (error ?? {}) as { name?: string; message?: string; stack?: string; status?: number; url?: string; rejection?: unknown };
  if (e.rejection !== undefined && e.rejection !== error) {
    return armarReporte(e.rejection, ruta, version, agente);
  }
  if (e.name === 'HttpErrorResponse' && e.status === 0) {
    return null;
  }

  const mensaje = typeof error === 'string' ? error : `${e.name ?? 'Error'}: ${e.message ?? String(error)}`;
  return {
    origen: 'web',
    mensaje: mensaje.slice(0, 1000),
    pila: typeof e.stack === 'string' ? e.stack.slice(0, 8000) : undefined,
    ruta: ruta.slice(0, 300),
    version: version.slice(0, 60),
    agente: agente.slice(0, 300),
  };
}

/** La versión desplegada: el hash del `main-XXXX.js` que cargó la página. */
export function versionDeLaWeb(documento: Document): string {
  const src = documento.querySelector<HTMLScriptElement>('script[src*="main"]')?.getAttribute('src') ?? '';
  return /main-([A-Za-z0-9]+)\.js/.exec(src)?.[1] ?? 'desarrollo';
}
