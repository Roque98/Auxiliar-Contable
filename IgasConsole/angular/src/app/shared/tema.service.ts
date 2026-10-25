import { Injectable, computed, effect, signal } from '@angular/core';

export type Tema = 'claro' | 'oscuro';

const CLAVE = 'ig-tema';

/**
 * El tema claro u oscuro del design system (`data-tema` en <html>, ver
 * `styles/igas/_tokens.scss`).
 *
 * Sin elección guardada se sigue al sistema operativo, y se le sigue EN VIVO: si el
 * teléfono pasa a oscuro al anochecer, la aplicación también. En cuanto la persona pulsa el
 * botón, manda su elección y se recuerda en este navegador.
 *
 * `index.html` aplica el mismo cálculo antes de que arranque Angular; si no, la página
 * pintaría blanco un instante antes de oscurecerse.
 */
@Injectable({ providedIn: 'root' })
export class TemaService {
  private readonly sistema = window.matchMedia('(prefers-color-scheme: dark)');
  private readonly elegido = signal<Tema | null>(leer());
  private readonly delSistema = signal<Tema>(this.sistema.matches ? 'oscuro' : 'claro');

  readonly tema = computed<Tema>(() => this.elegido() ?? this.delSistema());

  constructor() {
    this.sistema.addEventListener('change', e => this.delSistema.set(e.matches ? 'oscuro' : 'claro'));
    // La primera vez sin animar: al cargar la página el tema ya lo puso index.html.
    let primera = true;
    effect(() => {
      const tema = this.tema();
      if (primera) {
        primera = false;
        aplicar(tema);
      } else {
        aplicarConFundido(tema);
      }
    });
  }

  alternar(): void {
    const nuevo: Tema = this.tema() === 'oscuro' ? 'claro' : 'oscuro';
    this.elegido.set(nuevo);
    try {
      localStorage.setItem(CLAVE, nuevo);
    } catch {
      // Sin almacenamiento: la elección dura hasta cerrar la pestaña.
    }
  }
}

function leer(): Tema | null {
  try {
    const guardado = localStorage.getItem(CLAVE);
    return guardado === 'claro' || guardado === 'oscuro' ? guardado : null;
  } catch {
    return null;
  }
}

/** Duración del fundido. Igual que `--ig-cambio-tema` en styles/igas/_base.scss. */
const DURACION_FUNDIDO_MS = 750;

/**
 * Cambia de tema con un fundido en vez de un salto: el cambio instantáneo de toda la
 * pantalla de blanco a casi negro (o al revés) molesta a la vista.
 *
 * Con la API de transiciones de vista (Chrome, Edge, Safari recientes) el navegador funde
 * una «foto» de la pantalla vieja sobre la nueva, de una vez y sin estados intermedios
 * raros. Donde no existe, se animan los colores con una clase temporal en <html>.
 */
function aplicarConFundido(tema: Tema): void {
  const doc = document as Document & { startViewTransition?: (cambio: () => void) => unknown };
  if (typeof doc.startViewTransition === 'function') {
    doc.startViewTransition(() => aplicar(tema));
    return;
  }

  const raiz = document.documentElement;
  raiz.classList.add('ig-cambiando-tema');
  aplicar(tema);
  window.setTimeout(() => raiz.classList.remove('ig-cambiando-tema'), DURACION_FUNDIDO_MS + 50);
}

function aplicar(tema: Tema): void {
  const raiz = document.documentElement;
  raiz.setAttribute('data-tema', tema);
  // Bootstrap también: sus piezas nativas (flecha del select, botón de cerrar) cambian solas.
  raiz.setAttribute('data-bs-theme', tema === 'oscuro' ? 'dark' : 'light');
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', tema === 'oscuro' ? '#16181b' : '#ffffff');
}
