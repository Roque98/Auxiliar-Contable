import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { AsyncPipe } from '@angular/common';
import { map } from 'rxjs';
import { BrandingService } from '../branding/branding.service';

/**
 * El pie de página.
 *
 * <p>Sustituye al de la plantilla, que decía «© Lepton Theme by Volosoft» y ofrecía tres
 * enlaces —About, Privacy, Contact— que apuntaban a <c>#</c>, o sea a ninguna parte. Dos
 * problemas distintos en la misma barra: enlaces muertos en todas las pantallas, y un cartel
 * en el pie diciendo con qué está hecho el sitio. Esto último importa especialmente porque el
 * pie sale <b>también sin sesión</b>, y ahí es donde menos interesa declararlo (§6.98).</p>
 *
 * <p>El nombre sale de la marca del inquilino, igual que la barra superior y el título de la
 * pestaña: quien entra en su espacio ve el suyo, no el de la instalación.</p>
 */
@Component({
  selector: 'abp-footer',
  standalone: true,
  imports: [AsyncPipe],
  template: `
    <div class="lpx-footbar-container end-0">
      <div class="lpx-footbar">
        <div class="lpx-footbar-copyright">
          <span>{{ anio }}© {{ nombre$ | async }}</span>
        </div>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class FooterComponent {
  private readonly marca = inject(BrandingService);

  readonly anio = new Date().getFullYear();

  /** Reactivo: el nombre se edita en `/branding` y cambia sin recargar la página. */
  readonly nombre$ = this.marca.marca$.pipe(map(m => m.appName));
}
