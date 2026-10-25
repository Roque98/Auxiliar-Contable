import { ChangeDetectionStrategy, Component, inject, input, model, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { OrganizacionesService } from '../organizaciones/organizaciones.service';
import { OrganizacionResumenDto } from './membresias.service';

/**
 * «¿En qué organización entra?» para los formularios de alta (Invitaciones y Usuarios).
 * Un cliente sin organización no vería nada, así que el backend lo exige; esto lo pregunta a
 * tiempo y, a quien puede, le deja crear la organización sin salir del formulario.
 */
@Component({
  selector: 'app-selector-organizacion',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="so">
      <label class="so-etiqueta" [for]="idCampo()">
        Organización del cliente @if (requerida()) {<span class="so-req">obligatoria</span>}
      </label>

      @if (!creando()) {
        <div class="so-fila">
          <select class="ig-control" [id]="idCampo()" [name]="idCampo()" [ngModel]="valor()" (ngModelChange)="elegir($event)">
            <option [ngValue]="null" disabled>Elige a qué organización pertenece…</option>
            @for (o of organizaciones(); track o.id) {
              <option [ngValue]="o.id">{{ o.nombre }}</option>
            }
          </select>
          @if (puedeCrear()) {
            <button type="button" class="ig-btn ig-btn-secundario" (click)="creando.set(true)">
              <i class="bi bi-plus-lg" aria-hidden="true"></i>Nueva
            </button>
          }
        </div>
        @if (!organizaciones().length) {
          <p class="so-ayuda so-aviso">
            Aún no hay organizaciones.
            @if (puedeCrear()) {
              Crea la del cliente con «Nueva».
            } @else {
              Pide a un Administrador u Owner que la cree en Organizaciones.
            }
          </p>
        } @else {
          <p class="so-ayuda">Verá la información de esa organización, y nada más.</p>
        }
      } @else {
        <div class="so-fila">
          <input type="text" class="ig-control" [id]="idCampo()" name="so-nueva" maxlength="128"
                 placeholder="Nombre del cliente (p. ej. Ferretería del Centro)" [(ngModel)]="nombreNuevo"
                 (keydown.enter)="$event.preventDefault(); crear()" />
          <button type="button" class="ig-btn ig-btn-principal" [disabled]="!nombreNuevo.trim() || guardando()" (click)="crear()">
            Crear
          </button>
          <button type="button" class="ig-btn ig-btn-texto" (click)="creando.set(false)">Cancelar</button>
        </div>
        @if (error(); as e) {
          <p class="so-ayuda so-error" role="alert">{{ e }}</p>
        }
      }
    </div>
  `,
  styles: [
    `
      .so-etiqueta {
        display: block;
        margin-bottom: var(--ig-esp-1);
        font-weight: var(--ig-peso-semi);
        font-size: var(--ig-texto-sm);
      }
      .so-req {
        margin-left: var(--ig-esp-1);
        padding: 1px 8px;
        border-radius: var(--ig-radio-pastilla);
        background: var(--ig-acento-suave);
        color: var(--ig-acento-texto);
        font-size: var(--ig-texto-xs);
      }
      .so-fila {
        display: flex;
        gap: var(--ig-esp-2);
        flex-wrap: wrap;
      }
      .so-fila > .ig-control {
        flex: 1 1 240px;
        min-width: 0;
      }
      .so-ayuda {
        margin: var(--ig-esp-1) 0 0;
        font-size: var(--ig-texto-sm);
        color: var(--ig-texto-secundario);
      }
      .so-aviso {
        color: var(--ig-alerta-texto);
      }
      .so-error {
        color: var(--ig-error-texto);
      }
    `,
  ],
})
export class SelectorOrganizacionComponent {
  private readonly api = inject(OrganizacionesService);

  readonly organizaciones = input<OrganizacionResumenDto[]>([]);
  readonly puedeCrear = input(false);
  readonly requerida = input(true);
  readonly idCampo = input('selector-organizacion');
  readonly valor = model<string | null>(null);
  /** Una organización recién creada, para que el padre la añada a su lista. */
  readonly creada = output<OrganizacionResumenDto>();

  readonly creando = signal(false);
  readonly guardando = signal(false);
  readonly error = signal<string | null>(null);
  nombreNuevo = '';

  elegir(id: string | null): void {
    this.valor.set(id);
  }

  crear(): void {
    const nombre = this.nombreNuevo.trim();
    if (!nombre) {
      return;
    }
    this.guardando.set(true);
    this.error.set(null);
    this.api.create({ nombre }).subscribe({
      next: o => {
        this.guardando.set(false);
        this.creando.set(false);
        this.nombreNuevo = '';
        this.creada.emit({ id: o.id, nombre: o.nombre, puedeSubir: true });
        this.valor.set(o.id);
      },
      error: e => {
        this.guardando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo crear la organización.'));
      },
    });
  }
}
