import { ChangeDetectionStrategy, Component } from '@angular/core';
import { DynamicLayoutComponent } from '@abp/ng.core';
import { LoaderBarComponent } from '@abp/ng.theme.shared';
import { BuscadorComponent } from './shared/buscador.component';
import { SaltoAlContenidoComponent } from './shared/salto-al-contenido.component';

@Component({
  selector: 'app-root',
  template: `
    <!-- EL PRIMERO DEL TODO, y ahí está la gracia: tiene que ser el primer elemento que
         recibe el foco al pulsar Tab. Medido, sin él son 8 paradas atravesando el menú
         antes de llegar al contenido, en cada navegación. -->
    <app-salto-al-contenido />
    <!-- Fuera del router: el atajo Ctrl+K tiene que funcionar en cualquier pantalla. -->
    <app-buscador />
    <abp-loader-bar color="var(--ig-acento)" />
    <abp-dynamic-layout />
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [LoaderBarComponent, DynamicLayoutComponent, BuscadorComponent, SaltoAlContenidoComponent],
})
export class AppComponent {}
