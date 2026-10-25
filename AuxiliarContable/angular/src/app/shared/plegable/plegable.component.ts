import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  Injectable,
  OnInit,
  booleanAttribute,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { AlmacenPlegable, abiertaAlEmpezar, guardarPlegable, leerPlegable, siguienteCabecera } from './plegable';

/**
 * «Expandir todo / Colapsar todo» de un grupo de secciones. Una orden con contador: cada
 * sección del grupo la escucha (efecto) y se abre o se cierra; el contador hace que repetir
 * la misma orden vuelva a aplicarse aunque entretanto se tocara una a mano.
 */
@Injectable({ providedIn: 'root' })
export class PlegablesService {
  readonly orden = signal<{ grupo: string; abrir: boolean; n: number } | null>(null);
  private n = 0;

  todas(grupo: string, abrir: boolean): void {
    this.orden.set({ grupo, abrir, n: ++this.n });
  }
}

let siguienteId = 0;

/**
 * Una sección plegable accesible (patrón «acordeón» de WAI-ARIA): el título es un botón
 * dentro de un h2/h3 con `aria-expanded` y `aria-controls`; el cuerpo es una `region`
 * etiquetada por él y queda `inert` al cerrarse (ni foco ni lector de pantalla dentro).
 *
 * - Recuerda abierta/cerrada por `clave` en localStorage.
 * - `alerta`: con errores o avisos arranca abierta siempre y lo marca en la cabecera.
 * - Al imprimir (y en el PDF del navegador) todas salen abiertas.
 * - Flechas, Inicio y Fin saltan entre cabeceras del mismo grupo; Enter/Espacio pliegan.
 *
 * El contenido se proyecta siempre (no se destruye al cerrar), así que plegar no pierde el
 * estado de lo de dentro (pestañas, scroll de tablas).
 *
 * Uso: `<app-plegable clave="vol.bitacora" [porDefecto]="false" [alerta]="x">
 *   <span plegableTitulo>Bitácora</span> ...contenido... </app-plegable>`
 */
@Component({
  selector: 'app-plegable',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'sp',
    '[class.ig-panel]': 'panel()',
    '[class.sp-abierta]': 'abierta()',
    '[attr.data-alerta]': 'alerta() ? "" : null',
  },
  template: `
    <ng-template #boton>
      <button
        type="button"
        class="sp-boton"
        [id]="idBoton"
        [attr.data-grupo]="grupo()"
        [attr.aria-expanded]="abierta()"
        [attr.aria-controls]="idCuerpo"
        (click)="alternar()"
        (keydown)="tecla($event)"
      >
        <i class="bi bi-chevron-down sp-chevron" aria-hidden="true"></i>
        <span class="sp-titulo"><ng-content select="[plegableTitulo]" /></span>
        @if (alerta()) {
          <span class="sp-atencion" title="Tiene errores o avisos"><span class="sp-oculto">(requiere atención)</span></span>
        }
      </button>
    </ng-template>
    <div class="sp-cab">
      @if (nivel() === 3) {
        <h3 class="sp-h"><ng-container *ngTemplateOutlet="boton" /></h3>
      } @else {
        <h2 class="sp-h"><ng-container *ngTemplateOutlet="boton" /></h2>
      }
      <ng-content select="[plegableExtra]" />
    </div>
    <div class="sp-cuerpo" [id]="idCuerpo" role="region" [attr.aria-labelledby]="idBoton" [attr.inert]="abierta() ? null : ''">
      <div class="sp-interior"><ng-content /></div>
    </div>
  `,
  imports: [NgTemplateOutlet],
  styles: [
    `
      :host {
        display: block;
        min-width: 0;
      }
      .sp-cab {
        display: flex;
        align-items: center;
        gap: var(--ig-esp-2, 8px);
        flex-wrap: wrap;
      }
      .sp-h {
        flex: 1 1 14rem;
        min-width: 0;
        margin: 0;
      }
      .sp-boton {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        width: 100%;
        /* Objetivo táctil de 44 px (WCAG 2.5.5) también en móvil. */
        min-height: 44px;
        padding: 0;
        border: 0;
        background: none;
        color: inherit;
        font: inherit;
        letter-spacing: inherit;
        text-align: left;
        cursor: pointer;
        border-radius: var(--ig-radio-xs, 7px);
      }
      .sp-titulo {
        flex: 1;
        min-width: 0;
        overflow-wrap: anywhere;
      }
      .sp-chevron {
        flex: none;
        font-size: 0.85em;
        transition: transform 200ms ease;
        transform: rotate(-90deg);
      }
      :host(.sp-abierta) .sp-chevron {
        transform: none;
      }
      .sp-atencion {
        flex: none;
        width: 0.6rem;
        height: 0.6rem;
        border-radius: 50%;
        background: var(--ig-error, #c0392b);
      }
      .sp-oculto {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip: rect(0 0 0 0);
        white-space: nowrap;
      }
      /* La animación: de 0fr a 1fr en la fila de la rejilla, sin medir alturas en JS. Al
         cerrar, «visibility» espera a que termine para que no se vea un salto. */
      .sp-cuerpo {
        display: grid;
        grid-template-rows: 0fr;
        visibility: hidden;
        transition:
          grid-template-rows 220ms ease,
          visibility 0s linear 220ms;
      }
      :host(.sp-abierta) .sp-cuerpo {
        grid-template-rows: 1fr;
        visibility: visible;
        transition:
          grid-template-rows 220ms ease,
          visibility 0s;
      }
      .sp-interior {
        min-height: 0;
        overflow: hidden;
      }
      :host(.sp-abierta) .sp-interior {
        padding-top: var(--ig-esp-3, 12px);
      }
      @media print {
        .sp-cuerpo,
        :host(.sp-abierta) .sp-cuerpo {
          grid-template-rows: 1fr !important;
          visibility: visible !important;
          transition: none !important;
        }
        .sp-interior {
          overflow: visible;
          padding-top: var(--ig-esp-3, 12px);
        }
        .sp-chevron {
          display: none;
        }
        :host {
          break-inside: auto;
        }
      }
    `,
  ],
})
export class PlegableComponent implements OnInit {
  private readonly servicio = inject(PlegablesService);
  private readonly documento = inject(DOCUMENT);

  /** Dónde se recuerda: única por sección (p. ej. `vol.bitacora`). */
  readonly clave = input.required<string>();
  readonly porDefecto = input(true, { transform: booleanAttribute });
  /** Tiene errores o avisos: arranca abierta y se marca. */
  readonly alerta = input(false, { transform: booleanAttribute });
  /** Para «Expandir/Colapsar todo» y las flechas. */
  readonly grupo = input('general');
  readonly nivel = input<2 | 3>(2);
  /** Pinta la caja `ig-panel` (no, si va dentro de otra). */
  readonly panel = input(true, { transform: booleanAttribute });

  readonly abierta = signal(true);
  readonly idBoton = `sp-boton-${++siguienteId}`;
  readonly idCuerpo = `sp-cuerpo-${siguienteId}`;

  private iniciada = false;

  constructor() {
    // «Expandir/Colapsar todo»: solo órdenes nuevas (la que ya estaba al nacer no cuenta).
    const alNacer = untracked(() => this.servicio.orden()?.n ?? 0);
    effect(() => {
      const o = this.servicio.orden();
      if (o && o.n > alNacer && o.grupo === this.grupo()) {
        untracked(() => this.poner(o.abrir));
      }
    });
    // Si los avisos llegan después (datos que cargan más tarde), se abre.
    effect(() => {
      if (this.alerta() && untracked(() => this.iniciada)) {
        this.abierta.set(true);
      }
    });
  }

  ngOnInit(): void {
    this.abierta.set(
      abiertaAlEmpezar({ porDefecto: this.porDefecto(), guardado: leerPlegable(this.almacen(), this.clave()), alerta: this.alerta() }),
    );
    this.iniciada = true;
  }

  alternar(): void {
    this.poner(!this.abierta());
  }

  tecla(e: KeyboardEvent): void {
    const botones = Array.from(this.documento.querySelectorAll<HTMLButtonElement>(`.sp-boton[data-grupo="${this.grupo()}"]`));
    const destino = siguienteCabecera(botones.indexOf(e.currentTarget as HTMLButtonElement), botones.length, e.key);
    if (destino != null) {
      e.preventDefault();
      botones[destino].focus();
    }
  }

  private poner(abrir: boolean): void {
    this.abierta.set(abrir);
    guardarPlegable(this.almacen(), this.clave(), abrir);
  }

  private almacen(): AlmacenPlegable | null {
    try {
      return this.documento.defaultView?.localStorage ?? null;
    } catch {
      return null;
    }
  }
}

/** Botones «Expandir todo / Colapsar todo» de un grupo. No salen al imprimir. */
@Component({
  selector: 'app-plegables-controles',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="spc" role="group" aria-label="Secciones">
      <button type="button" class="ig-btn ig-btn-texto ig-btn-chico" (click)="todas(true)">
        <i class="bi bi-arrows-expand" aria-hidden="true"></i>Expandir todo
      </button>
      <button type="button" class="ig-btn ig-btn-texto ig-btn-chico" (click)="todas(false)">
        <i class="bi bi-arrows-collapse" aria-hidden="true"></i>Colapsar todo
      </button>
    </div>
  `,
  styles: [
    `
      .spc {
        display: flex;
        flex-wrap: wrap;
        justify-content: flex-end;
        gap: var(--ig-esp-2, 8px);
        margin-top: var(--ig-esp-3, 12px);
      }
      @media print {
        .spc {
          display: none;
        }
      }
    `,
  ],
})
export class PlegablesControlesComponent {
  private readonly servicio = inject(PlegablesService);
  readonly grupo = input('general');

  todas(abrir: boolean): void {
    this.servicio.todas(this.grupo(), abrir);
  }
}
