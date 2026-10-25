import { ConfigStateService, RestService } from '@abp/ng.core';
import { Injectable, Injector } from '@angular/core';
import { armarReporte, MAXIMO_POR_PAGINA, versionDeLaWeb } from './errores-del-cliente';

/**
 * Manda los errores de la web a la API (y de ahí a OpenObserve): solo con sesión, una vez por
 * mensaje y hasta {@link MAXIMO_POR_PAGINA} por carga. Nunca lanza: reportar no puede romper nada.
 *
 * ⚠️ Los servicios se piden al Injector AL REPORTAR, no en el constructor: el ErrorHandler se crea
 * muy pronto y HttpClient (con sus interceptores) puede depender de él — inyectarlo de entrada
 * arma un ciclo.
 */
@Injectable({ providedIn: 'root' })
export class ReporteDeErrores {
  private readonly vistos = new Set<string>();
  private enviados = 0;

  constructor(private readonly injector: Injector) {}

  reportar(error: unknown): void {
    try {
      const reporte = armarReporte(error, location.pathname, versionDeLaWeb(document), navigator.userAgent);
      if (!reporte || this.enviados >= MAXIMO_POR_PAGINA || this.vistos.has(reporte.mensaje)) {
        return;
      }
      if (!this.injector.get(ConfigStateService).getOne('currentUser')?.isAuthenticated) {
        return;
      }

      this.vistos.add(reporte.mensaje);
      this.enviados++;
      this.injector
        .get(RestService)
        .request({ method: 'POST', url: '/api/app/errores-del-cliente/registrar', body: reporte }, { apiName: 'Default', skipHandleError: true })
        .subscribe({ error: () => undefined });
    } catch {
      // Reportar nunca puede tumbar a quien reporta.
    }
  }
}
