import { ReplaceableComponentsService } from '@abp/ng.core';
import { inject, provideAppInitializer } from '@angular/core';
import { AuxiliarLayoutComponent } from './auxiliar-layout.component';

/**
 * El armazón de la consola en lugar del de LeptonX. ABP pinta cada pantalla dentro del layout
 * registrado con esta clave (verificado en `@abp/ng.theme.lepton-x`,
 * `eThemeLeptonXComponents.ApplicationLayout`), así que las pantallas de ABP —usuarios,
 * roles, ajustes— también salen dentro del nuestro.
 */
export const LAYOUT_PROVIDER = [
  provideAppInitializer(() => {
    inject(ReplaceableComponentsService).add({
      key: 'Theme.ApplicationLayoutComponent',
      component: AuxiliarLayoutComponent,
    });
  }),
];
