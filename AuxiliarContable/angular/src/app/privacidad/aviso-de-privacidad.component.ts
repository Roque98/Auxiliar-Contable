import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FALTAN_DATOS_DEL_RESPONSABLE, SECCIONES, VERSION_DEL_AVISO } from './aviso';

/** El texto del aviso. Lo usan la página pública `/privacidad` y el panel que pide aceptarlo. */
@Component({
  selector: 'app-texto-del-aviso',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (faltanDatos) {
      <div class="ig-alerta ig-alerta-aviso" role="note">
        Borrador: faltan los datos del despacho responsable. No uses este aviso con clientes reales todavía.
      </div>
    }
    @for (s of secciones; track s.titulo) {
      <section class="av-seccion">
        <h2>{{ s.titulo }}</h2>
        @for (p of s.parrafos ?? []; track $index) {<p>{{ p }}</p>}
        @if (s.lista) {
          <ul>
            @for (item of s.lista; track $index) {<li>{{ item }}</li>}
          </ul>
        }
        @for (p of s.notas ?? []; track $index) {<p>{{ p }}</p>}
      </section>
    }
    <p class="ig-ayuda">Versión vigente: {{ version }}.</p>
  `,
  styles: [
    `
      :host {
        display: block;
        text-align: left;
        line-height: 1.6;
      }
      .av-seccion h2 {
        font-size: var(--ig-texto-lg);
        font-weight: var(--ig-peso-semi);
        margin: var(--ig-esp-5) 0 var(--ig-esp-2);
      }
      .av-seccion p,
      .av-seccion ul {
        margin: 0 0 var(--ig-esp-3);
      }
      .av-seccion li {
        margin-bottom: var(--ig-esp-1);
      }
    `,
  ],
})
export class TextoDelAvisoComponent {
  readonly secciones = SECCIONES;
  readonly version = VERSION_DEL_AVISO;
  readonly faltanDatos = FALTAN_DATOS_DEL_RESPONSABLE;
}

/** `/privacidad`: PÚBLICA, se lee antes de tener cuenta (desde el registro y el login). */
@Component({
  selector: 'app-aviso-de-privacidad',
  standalone: true,
  imports: [TextoDelAvisoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Aviso de privacidad</h1>
    <div class="av-caja">
      <app-texto-del-aviso />
    </div>
  `,
  styles: [
    `
      h1 {
        font-size: var(--ig-titulo-2);
        font-weight: var(--ig-peso-semi);
        margin: 0 0 var(--ig-esp-5);
      }
      .av-caja {
        background: var(--ig-blanco);
        border: var(--ig-borde);
        border-radius: var(--ig-radio-lg);
        box-shadow: var(--ig-sombra-sutil);
        padding: var(--ig-esp-4);
      }
      @media (min-width: 576px) {
        .av-caja {
          padding: var(--ig-esp-6);
        }
      }
    `,
  ],
})
export class AvisoDePrivacidadComponent {}
