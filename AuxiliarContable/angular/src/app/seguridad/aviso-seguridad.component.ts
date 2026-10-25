import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { SeguridadService } from './seguridad.service';

const CLAVE_OCULTO = 'ig-aviso-seguridad-hasta';
/** «Más tarde» lo esconde estos días; luego vuelve, hasta que la cuenta esté completa. */
const DIAS_OCULTO = 7;

/**
 * El aviso de arriba que pide completar la seguridad de la cuenta (correo y teléfono) a
 * quien le falte. Es UNA línea: en el teléfono, el aviso grande de antes se comía el primer
 * tercio de cada pantalla. «Más tarde» lo esconde una semana (antes, solo hasta cerrar el
 * navegador: volvía en cada visita).
 */
@Component({
  selector: 'app-aviso-seguridad',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible(); as falta) {
      <div class="ig-alerta ig-alerta-aviso av-aviso" role="status">
        <i class="bi bi-shield-exclamation av-icono" aria-hidden="true"></i>
        <a class="av-texto" routerLink="/seguridad"><strong>Protege tu cuenta:</strong> {{ falta }}</a>
        <button type="button" class="av-cerrar" aria-label="Recordármelo en una semana" title="Recordármelo en una semana"
                (click)="ocultar()">
          <i class="bi bi-x-lg" aria-hidden="true"></i>
        </button>
      </div>
    }
  `,
  styles: [
    `
      .av-aviso {
        display: flex;
        gap: var(--ig-esp-2);
        align-items: center;
        margin-bottom: var(--ig-esp-4);
        padding-block: var(--ig-esp-2);
        font-size: var(--ig-texto-md);
      }
      .av-icono {
        font-size: 1.1rem;
      }
      .av-texto {
        flex: 1;
        min-width: 0;
        color: inherit;
        text-decoration: underline;
        text-underline-offset: 3px;
      }
      .av-cerrar {
        flex: none;
        display: grid;
        place-items: center;
        width: 32px;
        height: 32px;
        border: 0;
        border-radius: 50%;
        background: none;
        color: inherit;
        cursor: pointer;
      }
    `,
  ],
})
export class AvisoSeguridadComponent implements OnInit {
  private readonly api = inject(SeguridadService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  private readonly estado = toSignal(this.api.estado$, { initialValue: null });
  private readonly url = signal(this.router.url);
  private readonly oculto = signal(this.leerOculto());

  /** El texto del aviso, o null si no hay que enseñarlo. */
  readonly visible = computed(() => {
    const e = this.estado();
    if (!e || this.oculto() || this.url().startsWith('/seguridad')) {
      return null;
    }
    const falta: string[] = [];
    if (!e.correoConfirmado) {
      falta.push('verifica tu correo');
    }
    if (!e.telefono) {
      falta.push('registra tu teléfono');
    }
    if (!falta.length) {
      return null;
    }
    const texto = falta.join(' y ');
    return texto.charAt(0).toUpperCase() + texto.slice(1) + '.';
  });

  ngOnInit(): void {
    this.api.cargar().subscribe({ error: () => undefined });
    this.router.events
      .pipe(
        filter((e): e is NavigationEnd => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(e => this.url.set(e.urlAfterRedirects));
  }

  ocultar(): void {
    this.oculto.set(true);
    try {
      localStorage.setItem(CLAVE_OCULTO, String(Date.now() + DIAS_OCULTO * 86_400_000));
    } catch {
      // Sin almacenamiento: dura hasta recargar.
    }
  }

  private leerOculto(): boolean {
    try {
      return Number(localStorage.getItem(CLAVE_OCULTO) ?? 0) > Date.now();
    } catch {
      return false;
    }
  }
}
