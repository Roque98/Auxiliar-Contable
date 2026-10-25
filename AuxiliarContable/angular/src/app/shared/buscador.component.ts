import { CoreModule, LocalizationService } from '@abp/ng.core';
import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnDestroy,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { Subject, Subscription, debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { BuscadorService, Resultado } from './buscador.service';

/**
 * Paleta de comandos: <kbd>Ctrl</kbd>+<kbd>K</kbd> desde cualquier pantalla.
 *
 * <p>Escribes y saltas — a una pantalla, a un inquilino, a un usuario— sin pasar por el menú.
 * En una aplicación con catorce destinos detrás de dos submenús, es la diferencia entre
 * navegar y buscar.</p>
 *
 * <p>Vive en `AppComponent`, fuera del router, para que el atajo funcione esté donde esté el
 * usuario. Se monta una sola vez.</p>
 *
 * <p>⚠️ <b>Las pantallas se filtran, y no solo se ocultan.</b> Ofrecer un destino que el
 * guardia va a rechazar es peor que no ofrecerlo: parece una avería. Es el mismo filtro por
 * permiso que el menú y que los atajos del panel de inicio.</p>
 *
 * <p>⚠️ El atajo NO se captura cuando el foco está en un campo de texto salvo que sea el
 * propio buscador: Ctrl+K es también «borrar hasta el final de línea» en algunos entornos, y
 * robárselo a quien está escribiendo en un formulario es justo el tipo de detalle que hace que
 * la gente odie una aplicación.</p>
 */
/** Evento de ventana para abrir el buscador sin teclado (el botón de la barra superior). */
export const ABRIR_BUSCADOR = 'ig-abrir-buscador';

@Component({
  selector: 'app-buscador',
  standalone: true,
  imports: [CoreModule],
  template: `
    @if (abierto()) {
      <div class="fondo" (click)="cerrar()">
        <div class="panel" role="dialog" aria-modal="true" (click)="$event.stopPropagation()">
          <div class="entrada">
            <i class="bi bi-search" aria-hidden="true"></i>
            <input
              #campo
              type="text"
              autocomplete="off"
              [attr.aria-label]="'::Buscador:Titulo' | abpLocalization"
              [placeholder]="'::Buscador:Placeholder' | abpLocalization"
              [value]="texto()"
              (input)="escribir($any($event.target).value)" />
            <kbd>ESC</kbd>
          </div>

          @if (resultados().length === 0) {
            <p class="sin-nada">{{ '::Buscador:SinResultados' | abpLocalization }}</p>
          } @else {
            <ul class="lista">
              @for (r of resultados(); track r.ruta + r.etiqueta; let i = $index) {
                @if (i === 0 || resultados()[i - 1].grupo !== r.grupo) {
                  <li class="grupo">{{ r.grupo }}</li>
                }
                <li
                  class="opcion"
                  [class.activa]="i === indice()"
                  (mouseenter)="indice.set(i)"
                  (click)="ir(r)">
                  <i class="bi" [class]="'bi ' + r.icono" aria-hidden="true"></i>
                  <span class="opcion-texto">
                    <span>{{ r.etiqueta }}</span>
                    @if (r.detalle) {
                      <span class="opcion-detalle">{{ r.detalle }}</span>
                    }
                  </span>
                </li>
              }
            </ul>
          }
        </div>
      </div>
    }
  `,
  styles: [
    `
      .fondo {
        position: fixed;
        inset: 0;
        z-index: 2000;
        background: rgba(0, 0, 0, 0.45);
        display: flex;
        justify-content: center;
        /* ⚠️ flex-start, NO el "stretch" por defecto: sin esto el panel se estira hasta el
           borde inferior de la pantalla y con tres resultados queda un rectángulo blanco
           enorme y medio vacío. Se vio en la captura, no leyendo el CSS.
           (Y ojo con las comillas invertidas en los comentarios de este bloque: va dentro
           de un template literal, así que una sola lo cierra a mitad y el componente deja
           de compilar con un error que no menciona ni el archivo.) */
        align-items: flex-start;
        /* Arriba y no centrado: la lista crece hacia abajo y con el panel centrado saltaría
           de sitio en cada pulsación. */
        padding-top: 12vh;
      }

      .panel {
        width: min(38rem, calc(100vw - 2rem));
        max-height: 70vh;
        display: flex;
        flex-direction: column;
        /* ⚠️ --ig-blanco, NO --bs-body-bg: esa vale #161616 en LeptonX Lite y pintaría el
           panel negro con bordes claros. Ver CLAUDE.md §6.02.1. */
        background: var(--ig-blanco, #fff);
        border-radius: 0.85rem;
        box-shadow: 0 1.5rem 3rem rgba(0, 0, 0, 0.3);
        overflow: hidden;
      }

      .entrada {
        display: flex;
        align-items: center;
        gap: 0.6rem;
        padding: 0.85rem 1rem;
        border-bottom: 1px solid var(--bs-border-color, #dee2e6);
      }

      .entrada input {
        flex: 1;
        border: 0;
        outline: 0;
        background: transparent;
        color: inherit;
        font-size: 1rem;
        min-width: 0;
      }

      .entrada kbd {
        font-size: 0.7rem;
        opacity: 0.6;
      }

      .lista {
        list-style: none;
        margin: 0;
        padding: 0.35rem;
        overflow-y: auto;
      }

      .grupo {
        padding: 0.5rem 0.65rem 0.25rem;
        font-size: 0.7rem;
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: 0.07em;
        color: var(--bs-secondary-color, #6c757d);
      }

      .opcion {
        display: flex;
        align-items: center;
        gap: 0.6rem;
        padding: 0.5rem 0.65rem;
        border-radius: 0.5rem;
        cursor: pointer;
      }

      .opcion.activa {
        background: var(--ig-acento-suave);
      }

      .opcion i {
        color: var(--ig-tinta);
      }

      .opcion-texto {
        display: flex;
        flex-direction: column;
        min-width: 0;
      }

      .opcion-detalle {
        font-size: 0.75rem;
        color: var(--bs-secondary-color, #6c757d);
      }

      .sin-nada {
        margin: 0;
        padding: 1.5rem;
        text-align: center;
        color: var(--bs-secondary-color, #6c757d);
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BuscadorComponent implements OnInit, OnDestroy {
  private readonly servicio = inject(BuscadorService);
  private readonly textos = inject(LocalizationService);
  private readonly router = inject(Router);

  readonly abierto = signal(false);
  readonly texto = signal('');
  readonly indice = signal(0);
  readonly resultados = signal<Resultado[]>([]);

  private readonly teclea = new Subject<string>();
  private readonly subs = new Subscription();

  ngOnInit(): void {
    this.subs.add(
      this.teclea
        .pipe(
          // 200 ms: por debajo, escribir «inquilino» lanza nueve rondas de peticiones.
          debounceTime(200),
          distinctUntilChanged(),
          switchMap(q =>
            this.servicio.buscarDatos(q, {
              usuarios: this.textos.instant('::Menu:Users'),
            }),
          ),
        )
        .subscribe(datos => {
          this.resultados.set([...this.pantallas(this.texto()), ...datos]);
          this.indice.set(0);
        }),
    );
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  @HostListener('window:' + ABRIR_BUSCADOR)
  alPedirloDesdeFuera(): void {
    this.abrir();
  }

  @HostListener('document:keydown', ['$event'])
  atajo(e: KeyboardEvent): void {
    const objetivo = e.target as HTMLElement | null;
    const escribiendo =
      !!objetivo &&
      (objetivo.tagName === 'INPUT' || objetivo.tagName === 'TEXTAREA' || objetivo.isContentEditable);

    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      // Dentro del propio buscador sí se permite, para poder cerrarlo con el mismo atajo.
      if (escribiendo && !this.abierto()) {
        return;
      }
      e.preventDefault();
      this.abierto() ? this.cerrar() : this.abrir();
      return;
    }

    if (!this.abierto()) {
      return;
    }

    if (e.key === 'Escape') {
      e.preventDefault();
      this.cerrar();
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      this.mover(1);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      this.mover(-1);
    } else if (e.key === 'Enter') {
      const elegido = this.resultados()[this.indice()];
      if (elegido) {
        e.preventDefault();
        this.ir(elegido);
      }
    }
  }

  abrir(): void {
    this.abierto.set(true);
    this.texto.set('');
    this.indice.set(0);
    this.resultados.set(this.pantallas(''));

    // El foco tras el render: el input no existe hasta que la señal repinta.
    setTimeout(() => document.querySelector<HTMLInputElement>('.entrada input')?.focus());
  }

  cerrar(): void {
    this.abierto.set(false);
  }

  escribir(valor: string): void {
    this.texto.set(valor);
    // Las pantallas se filtran al instante; los datos esperan al debounce. Así la lista
    // responde a cada tecla aunque la red vaya lenta.
    this.resultados.set(this.pantallas(valor));
    this.indice.set(0);
    this.teclea.next(valor);
  }

  ir(r: Resultado): void {
    this.cerrar();
    this.router.navigateByUrl(r.ruta);
  }

  private mover(paso: number): void {
    const total = this.resultados().length;
    if (total === 0) {
      return;
    }
    // Da la vuelta: bajar en el último lleva al primero, que es lo que hace toda paleta.
    this.indice.set((this.indice() + paso + total) % total);
  }

  private pantallas(filtro: string): Resultado[] {
    const grupo = this.textos.instant('::Buscador:Pantallas');
    const q = filtro.trim().toLowerCase();

    return this.servicio
      .destinosPermitidos()
      .map(d => ({
        grupo,
        etiqueta: this.textos.instant(d.clave),
        ruta: d.ruta,
        icono: d.icono,
      }))
      .filter(r => !q || r.etiqueta.toLowerCase().includes(q));
  }
}
