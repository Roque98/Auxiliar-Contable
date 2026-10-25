import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

/**
 * «Aquí no hay nada», dicho de forma que se entienda por qué.
 *
 * <p>Antes esto era una frase suelta dentro de una celda de tabla —«No hay trabajos», «No hay
 * datos»— y produce una duda concreta: quien la lee no sabe si <b>no hay nada</b> o si
 * <b>algo se rompió</b>. En un panel de administración esa diferencia importa: una cola de
 * trabajos vacía puede significar «todo va bien» o «el worker no está corriendo», que es justo
 * la pregunta que uno se hace al abrirla (§6.0.1).</p>
 *
 * <p>Por eso admite una <b>pista</b> además del texto: el mensaje dice qué no hay, y la pista
 * dice qué significa o qué hacer. Es opcional porque no todas las pantallas tienen algo útil
 * que añadir, y una pista vacía de contenido es peor que ninguna.</p>
 */
@Component({
  selector: 'app-vacio',
  standalone: true,
  template: `
    <div class="vacio">
      <i class="bi" [class]="'bi ' + icono" aria-hidden="true"></i>
      <p class="vacio-texto">{{ texto }}</p>
      @if (pista) {
        <p class="vacio-pista">{{ pista }}</p>
      }
      <ng-content />
    </div>
  `,
  styles: [
    `
      /* El estado vacío del design system (\`.ig-vacio\`). */
      .vacio {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--ig-esp-1);
        padding: var(--ig-esp-9) var(--ig-esp-5);
        text-align: center;
        color: var(--ig-texto-secundario);
      }

      .vacio i {
        font-size: 2.6rem;
        color: var(--ig-gris-medio);
        margin-bottom: var(--ig-esp-2);
      }

      .vacio-texto {
        margin: 0;
        font-weight: var(--ig-peso-semi);
        color: var(--ig-tinta);
      }

      .vacio-pista {
        margin: 0;
        max-width: 46ch;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VacioComponent {
  @Input({ required: true }) texto = '';

  /** Qué significa que esté vacío, o qué hacer. Opcional a propósito. */
  @Input() pista?: string | null;

  @Input() icono = 'bi-inbox';
}
