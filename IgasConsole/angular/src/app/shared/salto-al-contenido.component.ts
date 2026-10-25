import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * «Saltar al contenido»: el primer elemento tabulable de la página.
 *
 * <p>⚠️ <b>Esto no lo detecta axe</b>, y por eso hace falta recorrer la aplicación con el
 * teclado de verdad. Medido en tres pantallas: <b>8 paradas de tabulador atravesando el menú
 * lateral antes de llegar al contenido</b>, y otra vez en cada navegación. Para quien usa el
 * teclado —o un lector de pantalla— eso es el equivalente a tener que pasar por el índice
 * entero cada vez que se abre una página.</p>
 *
 * <p>Es el criterio 2.4.1 de WCAG («evitar bloques»), y la forma canónica de cumplirlo.</p>
 *
 * <p><b>Cómo funciona.</b> Está siempre en el DOM —no se puede crear al pulsar Tab, porque
 * para entonces el foco ya se ha ido a otro sitio— pero fuera de la pantalla hasta que recibe
 * el foco. Al enfocarlo aparece arriba a la izquierda; al pulsarlo, el navegador mueve el foco
 * al contenido principal.</p>
 *
 * <p>⚠️ <b>No se usa `display: none` ni `visibility: hidden` para esconderlo</b>: las dos cosas
 * lo sacarían del orden de tabulación y entonces no habría nada que enfocar. Se aparta con
 * posición absoluta, que es lo que lo mantiene alcanzable.</p>
 *
 * <p>⚠️ El destino se busca con `querySelector` en el momento de pulsar y no con un `href`
 * fijo: el contenedor lo pinta el layout de ABP y su id no está garantizado. Si no se
 * encuentra, se enfoca el primer <c>main</c>; y si tampoco, no se hace nada — un salto que no
 * funciona es preferible a un error en consola.</p>
 */
@Component({
  selector: 'app-salto-al-contenido',
  standalone: true,
  template: `
    <a class="salto" href="#" (click)="saltar($event)">{{ texto }}</a>
  `,
  styles: [
    `
      .salto {
        position: absolute;
        top: 0;
        left: 0;
        z-index: 2000;
        /* Fuera de la pantalla, pero PRESENTE: sigue siendo tabulable. */
        transform: translateY(-200%);
        padding: 0.65rem 1.1rem;
        border-radius: 0 0 var(--ig-radio, 0.4rem) 0;
        background: var(--ig-acento, #1d4ed8);
        color: var(--ig-sobre-acento, #ffffff);
        font-weight: 600;
        text-decoration: none;
        transition: transform 0.12s ease;
      }

      .salto:focus {
        transform: translateY(0);
      }

      @media (prefers-reduced-motion: reduce) {
        .salto {
          transition: none;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SaltoAlContenidoComponent {
  /**
   * Sin `abpLocalization`: este componente vive fuera del router y se pinta en el primer
   * render, antes de que el diccionario esté cargado — saldría vacío, que en un enlace de
   * salto es peor que salir en un idioma. Es la misma razón por la que la pantalla de
   * «espacio no disponible» tampoco se traduce.
   */
  readonly texto = 'Saltar al contenido / Skip to content';

  saltar(evento: Event): void {
    evento.preventDefault();

    const destino =
      document.querySelector<HTMLElement>('.lpx-content-container') ??
      document.querySelector<HTMLElement>('main') ??
      document.querySelector<HTMLElement>('router-outlet')?.parentElement ??
      null;

    if (!destino) {
      return;
    }

    // `tabindex="-1"` para poder enfocar un contenedor que no es enfocable por naturaleza.
    // Se pone al vuelo y no en la plantilla porque el elemento lo pinta el tema.
    if (!destino.hasAttribute('tabindex')) {
      destino.setAttribute('tabindex', '-1');
    }

    destino.focus({ preventScroll: false });
  }
}
