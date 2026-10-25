import { ChangeDetectionStrategy, Component, ElementRef, OnInit, inject, signal, viewChild } from '@angular/core';
import { AuthService } from '@abp/ng.core';
import { TextoDelAvisoComponent } from './aviso-de-privacidad.component';
import { VERSION_DEL_AVISO } from './aviso';
import { PrivacidadService } from './privacidad.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';

/**
 * Pide aceptar el aviso de privacidad vigente a quien tiene sesión y no lo ha aceptado: quien
 * ya tenía cuenta cuando se publicó, quien no entró por invitación, y TODOS cuando el aviso
 * cambia. Tapa la consola hasta que se acepta (o se cierra la sesión): sin consentimiento no
 * se pueden tratar sus datos financieros.
 *
 * <p>Si la consulta falla (red, despliegue a medias) NO bloquea: se vuelve a preguntar en la
 * próxima carga. Dejar a todo el mundo fuera por un error transitorio sería peor.</p>
 */
@Component({
  selector: 'app-aceptar-aviso',
  standalone: true,
  imports: [TextoDelAvisoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (pendiente()) {
      <div class="ac-fondo">
        <div class="ac-panel" role="dialog" aria-modal="true" aria-labelledby="ac-titulo">
          <h1 id="ac-titulo" tabindex="-1" #titulo>Antes de seguir: aviso de privacidad</h1>
          <p>
            Para organizar tu información contable tratamos datos fiscales, financieros y patrimoniales.
            Léelo y acéptalo para continuar.
          </p>
          <div class="ac-texto" tabindex="0" aria-label="Texto del aviso de privacidad">
            <app-texto-del-aviso />
          </div>
          @if (error(); as e) {
            <div class="ig-alerta ig-alerta-error" role="alert">{{ e }}</div>
          }
          <div class="ac-acciones">
            <button type="button" class="ig-btn ig-btn-principal" [disabled]="enviando()" (click)="aceptar()">
              @if (enviando()) {<span class="ig-girando" aria-hidden="true"></span>}
              Acepto el aviso de privacidad
            </button>
            <button type="button" class="ig-btn ig-btn-secundario" (click)="salir()">Cerrar sesión</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [
    `
      /* Mobile first: la base es el teléfono; desde 576 px se centra y se ensancha. */
      .ac-fondo {
        position: fixed;
        inset: 0;
        z-index: var(--ig-z-aviso);
        display: grid;
        place-items: center;
        padding: var(--ig-esp-2);
        background: color-mix(in srgb, var(--ig-tinta) 55%, transparent);
      }
      .ac-panel {
        display: flex;
        flex-direction: column;
        gap: var(--ig-esp-3);
        width: 100%;
        max-height: calc(100dvh - 2 * var(--ig-esp-2));
        padding: var(--ig-esp-4);
        background: var(--ig-blanco);
        border-radius: var(--ig-radio-lg);
        box-shadow: var(--ig-sombra-sutil);
      }
      .ac-panel h1 {
        font-size: var(--ig-titulo-3);
        font-weight: var(--ig-peso-semi);
        margin: 0;
      }
      /* Recibe el foco por programa (para el lector de pantalla), no por teclado: sin marco. */
      .ac-panel h1:focus {
        outline: none;
      }
      .ac-panel p {
        margin: 0;
      }
      .ac-texto {
        flex: 1;
        min-height: 0;
        overflow-y: auto;
        border: var(--ig-borde);
        border-radius: var(--ig-radio);
        padding: 0 var(--ig-esp-4);
      }
      /* Teléfono: botones a todo el ancho, el principal arriba (al alcance del pulgar). */
      .ac-acciones {
        display: flex;
        flex-direction: column;
        gap: var(--ig-esp-2);
      }
      @media (min-width: 576px) {
        .ac-fondo {
          padding: var(--ig-esp-4);
        }
        .ac-panel {
          width: min(720px, 100%);
          max-height: calc(100dvh - 2 * var(--ig-esp-4));
          padding: var(--ig-esp-6);
        }
        /* Escritorio: en fila a la derecha, el principal al final (orden de lectura igual). */
        .ac-acciones {
          flex-direction: row-reverse;
          justify-content: flex-start;
        }
      }
    `,
  ],
})
export class AceptarAvisoComponent implements OnInit {
  private readonly api = inject(PrivacidadService);
  private readonly auth = inject(AuthService);
  private readonly titulo = viewChild<ElementRef<HTMLElement>>('titulo');

  readonly pendiente = signal(false);
  readonly enviando = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.estado().subscribe({
      next: e => {
        this.pendiente.set(!e.aceptada);
        if (!e.aceptada) {
          // El foco al diálogo: quien navega con teclado o lector de pantalla empieza ahí.
          setTimeout(() => this.titulo()?.nativeElement.focus());
        }
      },
      error: () => undefined,
    });
  }

  aceptar(): void {
    if (this.enviando()) {
      return;
    }
    this.error.set(null);
    this.enviando.set(true);
    this.api.aceptar(VERSION_DEL_AVISO).subscribe({
      next: () => {
        this.enviando.set(false);
        this.pendiente.set(false);
      },
      error: e => {
        this.enviando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo guardar tu aceptación. Inténtalo de nuevo.'));
      },
    });
  }

  salir(): void {
    this.auth.logout().subscribe();
  }
}
