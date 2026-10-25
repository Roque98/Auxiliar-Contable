import { AbpTitleStrategy } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { RouterStateSnapshot } from '@angular/router';
import { BrandingService } from './branding.service';

/**
 * El título de la pestaña, con el nombre del INQUILINO en vez del de la plantilla.
 *
 * ⚠️ Sin esto no basta con asignar `document.title`: ABP registra `AbpTitleStrategy`, que
 * lo reescribe **en cada navegación** con `::AppName` —el texto localizado del proyecto, el
 * mismo para todos los inquilinos—. El síntoma es de los que despistan: la pestaña se pone
 * bien al guardar y vuelve al nombre de la plantilla en cuanto se cambia de pantalla, sin
 * que nada falle ni aparezca en la consola. Se encontró automatizando el navegador.
 *
 * Se hereda en vez de implementar `TitleStrategy` desde cero para conservar lo que la clase
 * de ABP ya resuelve: el formato `Ruta | Aplicación`, la opción de suprimir el nombre del
 * proyecto (`DISABLE_PROJECT_NAME`) y el `effect` que vuelve a titular al cambiar de idioma.
 */
@Injectable({ providedIn: 'root' })
export class TituloConMarca extends AbpTitleStrategy {
  private readonly marca = inject(BrandingService);

  override updateTitle(routerState: RouterStateSnapshot): void {
    // Lo guarda la clase base para poder re-titular al cambiar el idioma; si no se
    // asignara, ese camino se quedaría con el estado de la navegación anterior.
    this.routerState = routerState;

    const nombre = this.marca.nombreActual();
    const clave = this.buildTitle(routerState);

    if (!clave) {
      this.title.setTitle(nombre);
      return;
    }

    let texto = this.localizationService.instant({ key: clave, defaultValue: clave });
    if (!this.disableProjectName) {
      texto += ` | ${nombre}`;
    }

    this.title.setTitle(texto);
  }
}
