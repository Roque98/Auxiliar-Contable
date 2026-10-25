import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnDestroy, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  AuthService,
  ConfigStateService,
  CoreModule,
  PermissionService,
} from '@abp/ng.core';
import { Subscription, interval } from 'rxjs';
import { BrandingService } from '../branding/branding.service';
import { NotificationDto, NotificationsService } from '../notifications/notifications.service';
import { SystemStatusDto, SystemStatusService } from '../system-status/system-status.service';

/** Un atajo del panel. `policy` vacío = lo ve cualquiera que haya iniciado sesión. */
interface Atajo {
  ruta: string;
  icono: string;
  clave: string;
  policy?: string;
}

/**
 * La primera pantalla.
 *
 * <p>Sustituye a la página «Getting Started» que trae la plantilla de ABP, que enlazaba al
 * foro de abp.io, al Discord y al libro de Amazon. Aparte de no decir nada sobre esta
 * instalación, era un cartel anunciando qué hay enfrente: la primera pantalla de un sitio es
 * justo donde no interesa declarar con qué está hecho.</p>
 *
 * <p>Lo que se enseña depende de <b>quién mira</b>, y es la única pantalla del proyecto donde
 * eso pasa:</p>
 *
 * <ul>
 *   <li><b>Sin sesión</b>: el nombre del espacio y un botón de entrar. Nada más — ni versión,
 *   ni tecnología, ni enlaces. Es también la pantalla que ve quien llegue a la raíz cuando el
 *   acceso está oculto (§6.98), así que cuanto menos diga, mejor.</li>
 *   <li><b>Con sesión</b>: avisos recientes y atajos, para todo el mundo; y las tarjetas de
 *   estado de la instalación solo para quien tenga <c>IgasConsole.SystemStatus</c>.</li>
 * </ul>
 *
 * <p>⚠️ <b>Las tarjetas de estado se piden solo si hay permiso</b>, en vez de pedirlas siempre
 * y esconder el resultado. Un administrador de inquilino no tiene ese permiso desde §6.965.2:
 * pedirlo igualmente le daría un 403 en cada carga de su pantalla de inicio, que ensucia el
 * registro de auditoría con fallos que no son fallos y hace ruido justo en la señal —
 * proporción de peticiones fallidas— que usa el bloqueo automático de IPs.</p>
 *
 * <p>⚠️ Todo cambio de estado va envuelto en <c>aplicar()</c>, como el resto de pantallas: la
 * pila de OAuth puede dejar el callback fuera de la zona de Angular y la vista se queda
 * congelada con los datos buenos ya en memoria (CLAUDE.md §5.1).</p>
 */
@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, CoreModule, RouterLink],
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss'],
})
export class HomeComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly permisos = inject(PermissionService);
  private readonly config = inject(ConfigStateService);
  private readonly marca = inject(BrandingService);
  private readonly sistema = inject(SystemStatusService);
  private readonly avisosServicio = inject(NotificationsService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  /** Mismo ritmo que `/system-status`: es lo que tarda el recolector en producir datos. */
  private static readonly RefrescoMs = 60_000;

  /** Cuántos avisos caben sin que esto deje de ser un resumen. */
  private static readonly MaxAvisos = 5;

  private readonly subs = new Subscription();

  nombreDelEspacio = '';
  usuario = '';
  estado?: SystemStatusDto;
  avisos: NotificationDto[] = [];
  puedeVerSistema = false;
  cargandoEstado = false;

  /**
   * Los atajos, en el orden en que se miran: primero lo que reclama atención, luego lo que se
   * administra, y al final lo de cada uno.
   *
   * ⚠️ Los `policy` son los mismos que en `route.provider.ts` y `app.routes.ts`. Se filtran
   * aquí también —y no solo en el menú— porque un atajo a una pantalla que el guardia va a
   * rechazar es peor que no ofrecerlo: parece una avería.
   */
  private static readonly Atajos: Atajo[] = [
    { ruta: '/organizaciones', icono: 'bi-building', clave: '::Menu:Organizaciones', policy: 'IgasConsole.Organizaciones' },
    { ruta: '/actividad', icono: 'bi-clock-history', clave: '::Menu:Actividad', policy: 'IgasConsole.Actividad' },
    { ruta: '/system-status', icono: 'bi-activity', clave: '::Menu:SystemStatus', policy: 'IgasConsole.SystemStatus' },
    { ruta: '/jobs', icono: 'bi-list-task', clave: '::Menu:Jobs', policy: 'IgasConsole.Jobs' },
    { ruta: '/metrics', icono: 'bi-graph-up', clave: '::Menu:Metrics', policy: 'IgasConsole.Metrics' },
    { ruta: '/security', icono: 'bi-shield-lock', clave: '::Menu:Security', policy: 'IgasConsole.Security.BlockedIps' },
    // `bi-building`, singular: `bi-buildings` NO existe en la versión de bootstrap-icons que
    // trae el proyecto, y una clase de icono inexistente no da error — deja el hueco vacío y
    // el atajo parece a medio hacer. Comprobado contra el .css del paquete.
    { ruta: '/branding', icono: 'bi-palette', clave: '::Menu:Branding', policy: 'IgasConsole.Branding' },
    { ruta: '/api-keys', icono: 'bi-key', clave: '::Menu:ApiKeys', policy: 'IgasConsole.ApiKeys' },
    { ruta: '/notifications', icono: 'bi-bell', clave: '::Menu:Notifications' },
    { ruta: '/sessions', icono: 'bi-laptop', clave: '::Menu:Sessions' },
    { ruta: '/profile-picture', icono: 'bi-person-circle', clave: '::Menu:ProfilePicture' },
  ];

  atajos: Atajo[] = [];

  get haEntrado(): boolean {
    return this.auth.isAuthenticated;
  }

  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.nombreDelEspacio = this.marca.nombreActual();

    // El nombre puede cambiar sin recargar (lo edita /branding), así que se sigue la fuente
    // reactiva en vez de leerlo una vez.
    this.subs.add(
      this.marca.marca$.subscribe(m =>
        this.aplicar(() => (this.nombreDelEspacio = m.appName || this.nombreDelEspacio)),
      ),
    );

    if (!this.haEntrado) {
      return;
    }

    this.usuario = this.nombreDelUsuario();

    // ⚠️ Reactivo, no una lectura suelta: los permisos llegan con
    // /application-configuration, que puede resolverse DESPUÉS de este ngOnInit. Leídos una
    // sola vez, el panel se pintaba sin las tarjetas de estado para quien sí tiene permiso, y
    // solo aparecían al navegar a otra pantalla y volver.
    this.subs.add(
      this.permisos.getGrantedPolicy$('IgasConsole.SystemStatus').subscribe(puede => {
        const nuevo = !!puede;
        const habiaCambiado = nuevo !== this.puedeVerSistema;

        this.aplicar(() => {
          this.puedeVerSistema = nuevo;
          this.atajos = HomeComponent.Atajos.filter(
            a => !a.policy || this.permisos.getGrantedPolicy(a.policy),
          );
        });

        if (nuevo && habiaCambiado) {
          this.cargarEstado();
        }
      }),
    );

    this.cargarAvisos();

    this.subs.add(
      interval(HomeComponent.RefrescoMs).subscribe(() => {
        this.cargarAvisos();
        if (this.puedeVerSistema) {
          this.cargarEstado();
        }
      }),
    );
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  entrar(): void {
    this.auth.navigateToLogin();
  }

  private nombreDelUsuario(): string {
    const actual = this.config.getOne('currentUser') as
      | { name?: string; userName?: string }
      | undefined;

    // Mismo criterio que la barra superior de LeptonX (`user.name || user.userName`), para
    // que el saludo y el avatar no digan cosas distintas. Ver §6.965.1: por eso la activación
    // de un inquilino BORRA el nombre que siembra ABP, que es literalmente «admin».
    return actual?.name || actual?.userName || '';
  }

  private cargarEstado(): void {
    this.cargandoEstado = true;

    this.sistema.get().subscribe({
      next: e =>
        this.aplicar(() => {
          this.estado = e;
          this.cargandoEstado = false;
        }),
      // Un sondeo perdido conserva lo último bueno y calla, igual que en /metrics y
      // /system-status: esta es la pantalla de inicio y no puede recibir a nadie con un error.
      error: () => this.aplicar(() => (this.cargandoEstado = false)),
    });
  }

  private cargarAvisos(): void {
    this.avisosServicio.getList(false, HomeComponent.MaxAvisos).subscribe({
      next: lista => this.aplicar(() => (this.avisos = lista ?? [])),
      error: () => this.aplicar(() => (this.avisos = [])),
    });
  }

  /** ¿Hay algo que reclame atención? Decide el tono de la cabecera de estado. */
  get todoEnOrden(): boolean {
    const e = this.estado;
    if (!e) {
      return true;
    }
    return (
      e.health === 'Healthy' &&
      e.backup.state === 'Ok' &&
      e.restoreCheck.state !== 'Fallando' &&
      e.abandonedJobs === 0
    );
  }

  claseAviso(severidad: string): string {
    switch (severidad) {
      case 'Error':
        return 'text-danger';
      case 'Warning':
        return 'text-warning';
      default:
        return 'text-body-secondary';
    }
  }

  iconoAviso(severidad: string): string {
    switch (severidad) {
      case 'Error':
        return 'bi-exclamation-octagon-fill';
      case 'Warning':
        return 'bi-exclamation-triangle-fill';
      default:
        return 'bi-info-circle-fill';
    }
  }

  /** «hace 5 min», «ayer». Un instante exacto en un resumen no aporta nada. */
  desde(iso: string): string {
    const ms = Date.now() - new Date(iso).getTime();
    const min = Math.floor(ms / 60000);

    if (min < 1) return 'ahora';
    if (min < 60) return `hace ${min} min`;

    const h = Math.floor(min / 60);
    if (h < 24) return `hace ${h} h`;

    const d = Math.floor(h / 24);
    return d === 1 ? 'ayer' : `hace ${d} días`;
  }

  enMarcha(segundos: number): string {
    if (!segundos) {
      return '—';
    }
    const d = Math.floor(segundos / 86400);
    const h = Math.floor((segundos % 86400) / 3600);
    if (d > 0) return `${d} d ${h} h`;
    if (h > 0) return `${h} h`;
    return `${Math.floor(segundos / 60)} min`;
  }
}
