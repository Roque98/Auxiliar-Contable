import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

/**
 * Lo que se ve mientras cargan los datos: barras grises con la forma de lo que va a venir.
 *
 * <p>Sustituye a la palabra «Cargando…», que estaba en <b>doce</b> sitios. El problema de ese
 * texto no era que fuese feo: varias de estas pantallas <b>sondean cada minuto</b>
 * (`/metrics`, `/system-status`, `/jobs`), así que un mensaje que aparece y desaparece produce
 * un parpadeo constante y hace que el contenido salte de sitio en cada vuelta. Un esqueleto
 * ocupa el hueco que va a ocupar el dato, así que la página no se mueve.</p>
 *
 * <p>⚠️ <b>Solo debe pintarse en la PRIMERA carga</b>, no en cada refresco. El criterio que ya
 * usan estas pantallas —conservar lo último bueno y no vaciar la vista si un sondeo falla— es
 * el mismo: quien mira una tabla no quiere que se convierta en barras grises cada sesenta
 * segundos. Por eso las llamadas van con `*ngIf="cargando && !hayDatos"`.</p>
 *
 * <p>Lleva `aria-busy` y un texto solo para lectores de pantalla: visualmente es una forma,
 * pero para quien no la ve tiene que seguir diciendo que se está cargando.</p>
 */
@Component({
  selector: 'app-esqueleto',
  standalone: true,
  template: `
    <div class="esqueleto" role="status" aria-busy="true">
      @for (fila of filasArray; track $index) {
        <div class="esqueleto-fila" [style.width.%] = "anchos[$index % anchos.length]"></div>
      }
      <span class="visually-hidden">{{ etiqueta }}</span>
    </div>
  `,
  styles: [
    `
      .esqueleto {
        display: flex;
        flex-direction: column;
        gap: 0.55rem;
        padding: 0.25rem 0;
      }

      .esqueleto-fila {
        height: 1.05rem;
        border-radius: 6px;
        /* El degradado se mueve de izquierda a derecha: es lo que distingue «cargando» de
           «esto está roto y se quedó gris». */
        background: linear-gradient(
          90deg,
          var(--ig-gris-fondo) 25%,
          var(--ig-fondo) 37%,
          var(--ig-gris-fondo) 63%
        );
        background-size: 400% 100%;
        animation: esqueleto-brillo 1.4s ease infinite;
      }

      @keyframes esqueleto-brillo {
        0% {
          background-position: 100% 50%;
        }
        100% {
          background-position: 0 50%;
        }
      }

      /* Quien pide menos movimiento no debería recibir una animación en bucle. */
      @media (prefers-reduced-motion: reduce) {
        .esqueleto-fila {
          animation: none;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EsqueletoComponent {
  /** Cuántas barras. Por defecto, las de una tabla corta. */
  @Input() filas = 4;

  /** Para lectores de pantalla. Lo pasa quien lo usa, ya localizado. */
  @Input() etiqueta = '';

  /** Anchos irregulares: todas iguales parecen una tabla vacía, no algo cargando. */
  readonly anchos = [100, 82, 91, 70, 95, 78];

  get filasArray(): number[] {
    return Array.from({ length: Math.max(1, this.filas) });
  }
}
