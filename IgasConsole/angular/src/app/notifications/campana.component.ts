import { CommonModule } from '@angular/common';
import {
  ChangeDetectorRef,
  Component,
  NgZone,
  OnDestroy,
  OnInit,
  inject,
} from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { ConfigStateService } from '@abp/ng.core';
import { Subscription, interval } from 'rxjs';
import { NotificationDto, NotificationsService } from './notifications.service';

/**
 * La campana de la barra superior.
 *
 * Se engancha con `NavItemsService` (ver notificaciones.provider.ts), que es el punto que
 * LeptonX ofrece para meter cosas propias en la barra — el mismo que usan el selector de
 * idioma y el menú de usuario.
 */
@Component({
  selector: 'app-campana',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="campana" *ngIf="autenticado">
      <button
        type="button"
        class="boton"
        [attr.aria-label]="etiqueta"
        [title]="etiqueta"
        (click)="alternar()">
        <i class="bi bi-bell" aria-hidden="true"></i>
        <span *ngIf="sinLeer > 0" class="contador">{{ sinLeer > 99 ? '99+' : sinLeer }}</span>
      </button>

      <div *ngIf="abierta" class="panel">
        <div class="cabecera">
          <strong>Avisos</strong>
          <button *ngIf="sinLeer > 0" type="button" class="enlace" (click)="marcarTodo()">
            Marcar todo como leído
          </button>
        </div>

        <p *ngIf="avisos.length === 0" class="vacio">No hay avisos.</p>

        <button
          *ngFor="let a of avisos"
          type="button"
          class="aviso"
          [class.no-leido]="!a.isRead"
          (click)="abrir(a)">
          <span class="punto" [ngClass]="claseSeveridad(a.severity)" aria-hidden="true"></span>
          <span class="texto">
            <span class="titulo">{{ a.title }}</span>
            <span class="cuando">{{ cuando(a.creationTime) }}</span>
          </span>
        </button>

        <a class="ver-todos" routerLink="/notifications" (click)="abierta = false">
          Ver todos
        </a>
      </div>
    </div>
  `,
  styles: [
    `
      .campana {
        position: relative;
        display: flex;
        align-items: center;
      }
      /* El icono necesita sitio propio: con un botón pequeño el número se le monta
         encima y desde fuera parece que la campana no se pinta. */
      .boton {
        background: none;
        border: 0;
        padding: 0.4rem 0.6rem;
        font-size: 1.05rem;
        position: relative;
        color: inherit;
        cursor: pointer;
      }
      .contador {
        position: absolute;
        top: -0.1rem;
        right: -0.1rem;
        min-width: 1.1rem;
        padding: 0 0.25rem;
        border-radius: 999px;
        background: #dc3545;
        color: #fff;
        font-size: 0.68rem;
        line-height: 1.1rem;
        font-weight: 600;
      }
      .panel {
        position: absolute;
        top: calc(100% + 8px);
        right: 0;
        z-index: 1050;
        width: min(22rem, 90vw);
        max-height: 26rem;
        overflow-y: auto;
        background: var(--ig-blanco);
        color: var(--ig-tinta);
        border: var(--ig-borde);
        border-radius: var(--ig-radio-md);
        box-shadow: var(--ig-sombra-flotante);
      }

      /* En móvil la campana no está pegada al borde (a su derecha va el avatar): anclado
         a ella, el panel se salía por la izquierda. Aquí se fija bajo la barra superior
         con el mismo margen a los dos lados. */
      @media (max-width: 760px) {
        .panel {
          position: fixed;
          top: calc(var(--ig-alto-barra) + env(safe-area-inset-top, 0px) + 6px);
          left: 12px;
          right: 12px;
          width: auto;
          max-height: calc(100dvh - var(--ig-alto-barra) - 24px);
        }
      }
      .cabecera {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 0.5rem;
        padding: 0.6rem 0.8rem;
        border-bottom: 1px solid var(--bs-border-color, #dee2e6);
      }
      .enlace {
        background: none;
        border: 0;
        padding: 0;
        font-size: 0.8rem;
        color: var(--bs-link-color, #0d6efd);
        cursor: pointer;
      }
      .vacio {
        margin: 0;
        padding: 1rem 0.8rem;
        color: var(--bs-secondary-color, #6c757d);
      }
      .aviso {
        display: flex;
        gap: 0.55rem;
        width: 100%;
        padding: 0.6rem 0.8rem;
        background: none;
        border: 0;
        border-bottom: 1px solid var(--bs-border-color, #dee2e6);
        text-align: left;
        cursor: pointer;
      }
      .aviso.no-leido {
        background: var(--bs-tertiary-bg, #f1f3f5);
      }
      .punto {
        flex: 0 0 auto;
        width: 0.5rem;
        height: 0.5rem;
        margin-top: 0.35rem;
        border-radius: 50%;
      }
      .punto.info {
        background: #0d6efd;
      }
      .punto.warning {
        background: #fd7e14;
      }
      .punto.error {
        background: #dc3545;
      }
      .texto {
        display: flex;
        flex-direction: column;
        min-width: 0;
      }
      .titulo {
        font-size: 0.88rem;
        overflow-wrap: anywhere;
      }
      .cuando {
        font-size: 0.75rem;
        color: var(--bs-secondary-color, #6c757d);
      }
      .ver-todos {
        display: block;
        padding: 0.6rem 0.8rem;
        text-align: center;
        font-size: 0.85rem;
      }
    `,
  ],
})
export class CampanaComponent implements OnInit, OnDestroy {
  private readonly servicio = inject(NotificationsService);
  private readonly config = inject(ConfigStateService);
  private readonly router = inject(Router);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  /** Cada cuánto se vuelve a preguntar por el contador. */
  private static readonly SondeoMs = 60_000;

  private readonly subs = new Subscription();

  autenticado = false;
  abierta = false;
  sinLeer = 0;
  avisos: NotificationDto[] = [];

  get etiqueta(): string {
    return this.sinLeer > 0 ? `Avisos (${this.sinLeer} sin leer)` : 'Avisos';
  }

  /**
   * ⚠️ Ver CLAUDE.md §5.1: un callback de la pila de OAuth de ABP puede correr FUERA de la
   * zona de Angular, y entonces los datos llegan pero la vista no se repinta. Aquí se nota
   * el doble, porque lo que no se repinta es un número que la gente mira de reojo.
   */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.subs.add(
      this.config.getOne$('currentUser').subscribe(usuario => {
        const antes = this.autenticado;
        this.aplicar(() => {
          this.autenticado = !!usuario?.isAuthenticated;
          if (!this.autenticado) {
            this.sinLeer = 0;
            this.avisos = [];
            this.abierta = false;
          }
        });
        // Al entrar, el contador se pide ya: esperar al primer sondeo dejaría la campana
        // a cero durante un minuto justo después de iniciar sesión.
        if (this.autenticado && !antes) {
          this.refrescar();
        }
      }),
    );

    this.subs.add(this.servicio.sinLeer$.subscribe(n => this.aplicar(() => (this.sinLeer = n))));

    this.subs.add(
      interval(CampanaComponent.SondeoMs).subscribe(() => {
        if (this.autenticado) {
          this.refrescar();
        }
      }),
    );
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  private refrescar(): void {
    // Un sondeo perdido no puede levantar un modal ni matar el refresco: se ignora y se
    // reintenta al minuto siguiente.
    this.servicio.refrescarContador().subscribe({ error: () => undefined });
  }

  alternar(): void {
    this.abierta = !this.abierta;
    if (!this.abierta) {
      return;
    }
    this.servicio.getList(false, 8).subscribe({
      next: filas => this.aplicar(() => (this.avisos = filas)),
      error: () => this.aplicar(() => (this.avisos = [])),
    });
  }

  abrir(aviso: NotificationDto): void {
    this.abierta = false;

    const irA = () => {
      if (aviso.url) {
        this.router.navigateByUrl(aviso.url);
      }
    };

    if (aviso.isRead) {
      irA();
      return;
    }

    // Se marca leído al abrirlo, no al desplegar la campana: abrir la lista para echar un
    // vistazo no significa haber atendido nada.
    this.servicio.markRead([aviso.id]).subscribe({
      next: () => {
        this.servicio.refrescarContador().subscribe({ error: () => undefined });
        irA();
      },
      // Que falle el marcado no puede impedir llegar a donde apunta el aviso.
      error: () => irA(),
    });
  }

  marcarTodo(): void {
    this.servicio.markAllRead().subscribe({
      next: () => {
        this.servicio.refrescarContador().subscribe({ error: () => undefined });
        this.aplicar(() => (this.avisos = this.avisos.map(a => ({ ...a, isRead: true }))));
      },
      error: () => undefined,
    });
  }

  claseSeveridad(severidad: string): string {
    return severidad.toLowerCase();
  }

  cuando(iso: string): string {
    return new Date(iso).toLocaleString(undefined, {
      dateStyle: 'short',
      timeStyle: 'short',
    });
  }
}
