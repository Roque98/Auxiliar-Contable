import { inject, provideAppInitializer } from '@angular/core';
import { NavItem, NavItemsService } from '@abp/ng.theme.shared';
import { CampanaComponent } from './campana.component';

/**
 * Mete la campana en la barra superior de LeptonX.
 *
 * `NavItemsService` es el punto de extensión que ofrece el tema para eso — el mismo por el
 * que entran el selector de idioma y el menú de usuario. Un `NavItem` admite un componente
 * propio, así que no hace falta sustituir la barra entera (que además dejaría de
 * actualizarse con el módulo).
 *
 * El orden la deja a la IZQUIERDA del usuario y del idioma: lo que cambia solo va antes que
 * lo que solo se toca a mano.
 */
export const CAMPANA_PROVIDER = [
  provideAppInitializer(() => {
    const navItems = inject(NavItemsService);

    navItems.addItems([
      new NavItem({
        id: 'IgasConsole.Campana',
        order: 1,
        component: CampanaComponent,
      }),
    ]);
  }),
];
