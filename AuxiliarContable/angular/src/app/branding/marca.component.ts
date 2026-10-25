import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnDestroy, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { BrandingService } from './branding.service';

/**
 * La marca de la barra lateral: logo y nombre del inquilino.
 *
 * Sustituye a `lpx-brand-logo` del tema a través del punto de extensión
 * `Theme.LogoComponent` (ver `branding.provider.ts`). Hacía falta un componente propio y no
 * bastaba con redefinir la variable CSS del logo porque **el nombre no sale de CSS**: el
 * `BrandLogoComponent` de LeptonX lo inyecta de `LOGO_APP_NAME_TOKEN`, que es un valor
 * **fijo** tomado de `environment` al arrancar la aplicación y no puede cambiar con el
 * inquilino.
 *
 * El marcado replica el del tema (`.lpx-brand-logo` + `.lpx-brand-name` dentro de un enlace
 * a la raíz) para heredar sus tamaños, posiciones y comportamiento al plegar la barra. Sin
 * logo propio no se pone `background-image` y la regla del tema pinta el suyo, que es
 * exactamente lo que debe verse.
 */
@Component({
  selector: 'app-marca',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <a routerLink="/" class="text-decoration-none">
      <div
        class="lpx-brand-logo"
        role="img"
        aria-label="Logo"
        [style.background-image]="logo ? 'url(' + logo + ')' : null"
      ></div>
      <div class="lpx-brand-name" style="left: 58px !important">{{ nombre }}</div>
    </a>
  `,
})
export class MarcaComponent implements OnInit, OnDestroy {
  private readonly marca = inject(BrandingService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  private readonly subs = new Subscription();

  logo: string | null = null;
  nombre = '';

  ngOnInit(): void {
    this.subs.add(
      this.marca.marca$.subscribe(m =>
        // Ver CLAUDE.md §5.1: la pila de OAuth puede dejar la emisión fuera de la zona de
        // Angular, y entonces esto se actualizaría en memoria sin repintarse. Aquí se
        // notaría como una barra lateral que sigue con el nombre del inquilino anterior.
        this.zona.run(() => {
          this.logo = m.logoUrl;
          this.nombre = m.appName;
          this.cdr.detectChanges();
        }),
      ),
    );
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }
}
