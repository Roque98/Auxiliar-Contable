import { AsyncPipe, NgClass, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { IsActiveMatchOptions, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ABP, AuthService, ConfigStateService, LocalizationService, RoutesService, TreeNode } from '@abp/ng.core';
import type { SeccionMenu as SeccionMenuLocal } from './menu';
import { filter } from 'rxjs';
import { BrandingService } from '../branding/branding.service';
import { CampanaComponent } from '../notifications/campana.component';
import { ProfilePictureService } from '../profile-picture/profile-picture.service';
import { ABRIR_BUSCADOR } from '../shared/buscador.component';
import { TemaService } from '../shared/tema.service';
import { AvisoSeguridadComponent } from '../seguridad/aviso-seguridad.component';
import { AceptarAvisoComponent } from '../privacidad/aceptar-aviso.component';
import { casaConRuta, clavesQueContienen, construirMenu, idDeGrupo, leerAbiertos } from './menu';

export type { EntradaMenu, ItemMenu, SeccionMenu, SubgrupoMenu } from './menu';

const CLAVE_MENU_CERRADO = 'ig-menu-cerrado';
/** Qué secciones y subgrupos del menú dejó abiertos el usuario (todos arrancan cerrados). */
const CLAVE_MENU_ABIERTOS = 'ig-menu-abiertos';
const RUTAS_PUBLICAS = ['/registro', '/privacidad'];

export function iniciales(nombre: string): string {
  return (
    nombre
      .trim()
      .split(/\s+/)
      .slice(0, 2)
      .map(p => p[0] ?? '')
      .join('')
      .toUpperCase() || '?'
  );
}

/**
 * El armazón de la aplicación: el del prototipo de ZIA-Console (de donde nació), en lugar del de
 * LeptonX. Lo registra `LAYOUT_PROVIDER` como el layout «application» de ABP, así que
 * todas las pantallas —las nuestras y las de ABP— salen dentro de él.
 */
@Component({
  selector: 'app-auxiliar-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgClass, NgTemplateOutlet, AsyncPipe, CampanaComponent, AvisoSeguridadComponent, AceptarAvisoComponent],
  templateUrl: './auxiliar-layout.component.html',
  styleUrls: ['./auxiliar-layout.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuxiliarLayoutComponent implements OnInit {
  private readonly rutas = inject(RoutesService);
  private readonly textos = inject(LocalizationService);
  private readonly config = inject(ConfigStateService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly marca = inject(BrandingService).marca$;
  readonly temas = inject(TemaService);
  readonly foto = toSignal(inject(ProfilePictureService).foto$, { initialValue: null });

  private readonly arbol = toSignal(this.rutas.visible$, { initialValue: [] as TreeNode<ABP.Route>[] });
  private readonly usuario = toSignal(this.config.getOne$('currentUser'), { initialValue: null });
  private readonly url = signal(this.router.url);

  readonly menu = computed(() => construirMenu(this.arbol(), c => this.textos.instant(c)));

  readonly nombreUsuario = computed(() => {
    const u = this.usuario();
    const completo = [u?.name, u?.surName].filter(Boolean).join(' ');
    return completo || u?.userName || '';
  });

  readonly correo = computed(() => this.usuario()?.email ?? '');
  readonly iniciales = computed(() => iniciales(this.nombreUsuario()));

  /** El título de la barra superior: el enlace del menú que mejor casa con la URL. */
  readonly tituloPagina = computed(() => {
    const url = this.url();
    const todos = [...this.menu().secciones.flatMap(s => s.items), ...this.menu().cuenta];
    const candidato = todos
      .filter(i => casaConRuta(i.ruta, url))
      .sort((a, b) => b.ruta.length - a.ruta.length)[0];
    return candidato?.nombre ?? '';
  });

  /**
   * Secciones y subgrupos abiertos. Arrancan cerrados; se recuerda lo que abre o cierra el
   * usuario, y al navegar se abre siempre el que contiene la pantalla actual (si no, en un
   * enlace directo el resaltado quedaría escondido).
   */
  readonly abiertos = signal<Set<string>>(this.leerAbiertos());
  readonly idDeGrupo = idDeGrupo;

  /** La portada (el chat) lleva ?cliente=: con «exact» a secas dejaba de resaltarse en el menú. */
  readonly opcionesDeLaPortada: IsActiveMatchOptions = { paths: 'exact', queryParams: 'ignored', matrixParams: 'ignored', fragment: 'ignored' };

  constructor() {
    // Al llegar el árbol (los permisos cargan después) y en cada navegación.
    effect(() => this.abrirLoDeLaRuta(this.menu().secciones, this.url()));
  }

  abierto(clave: string | undefined): boolean {
    return !clave || this.abiertos().has(clave);
  }

  alternarGrupo(clave: string): void {
    const nuevo = new Set(this.abiertos());
    if (!nuevo.delete(clave)) {
      nuevo.add(clave);
    }
    this.abiertos.set(nuevo);
    this.guardarAbiertos(nuevo);
  }

  private abrirLoDeLaRuta(secciones: SeccionMenuLocal[], url: string): void {
    const faltan = clavesQueContienen(secciones, url).filter(c => !untracked(this.abiertos).has(c));
    if (faltan.length) {
      const nuevo = new Set([...untracked(this.abiertos), ...faltan]);
      this.abiertos.set(nuevo);
      this.guardarAbiertos(nuevo);
    }
  }

  private leerAbiertos(): Set<string> {
    try {
      return leerAbiertos(localStorage.getItem(CLAVE_MENU_ABIERTOS));
    } catch {
      return new Set();
    }
  }

  private guardarAbiertos(abiertos: Set<string>): void {
    try {
      localStorage.setItem(CLAVE_MENU_ABIERTOS, JSON.stringify([...abiertos]));
    } catch {
      // Sin almacenamiento: dura la sesión.
    }
  }

  /** Escritorio: el menú se puede plegar. Móvil: se abre por encima con un velo. */
  readonly cerrado = signal(this.leerPreferencia());
  readonly abiertoMovil = signal(false);
  // ⚠️ Mismo corte que el @media del .scss de este componente. 900 y no 760: con 760 una
  // tablet en vertical (768–834 px) se quedaba con el menú fijo comiéndose un tercio.
  private readonly consultaMovil = window.matchMedia('(max-width: 900px)');
  readonly movil = signal(this.consultaMovil.matches);

  /** La hamburguesa enseña la X cuando el menú está a la vista, sea en escritorio o en móvil. */
  readonly menuVisible = computed(() => (this.movil() ? this.abiertoMovil() : !this.cerrado()));

  readonly autenticado = computed(() => !!this.usuario()?.isAuthenticated);

  /** Rutas para quien aún no tiene cuenta: se pintan sin menú, haya sesión o no. */
  /** La guía de instalación del nodo: un documento largo, no un formulario: va a todo lo ancho. */
  readonly esPublica = computed(() => RUTAS_PUBLICAS.some(r => this.url().split(/[?#]/)[0] === r));
  readonly menuSesion = signal<'lateral' | 'barra' | null>(null);

  ngOnInit(): void {
    const alCambiar = (e: MediaQueryListEvent) => {
      this.movil.set(e.matches);
      this.abiertoMovil.set(false);
    };
    this.consultaMovil.addEventListener('change', alCambiar);
    this.destroyRef.onDestroy(() => this.consultaMovil.removeEventListener('change', alCambiar));

    this.router.events
      .pipe(
        filter((e): e is NavigationEnd => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(e => {
        this.url.set(e.urlAfterRedirects);
        this.abiertoMovil.set(false);
        this.menuSesion.set(null);
      });
  }

  alternarMenu(): void {
    if (this.movil()) {
      this.abiertoMovil.update(v => !v);
      return;
    }
    this.cerrado.update(v => !v);
    try {
      localStorage.setItem(CLAVE_MENU_CERRADO, this.cerrado() ? '1' : '0');
    } catch {
      // Sin almacenamiento (modo privado estricto): la preferencia dura la sesión.
    }
  }

  cerrarMovil(): void {
    this.abiertoMovil.set(false);
  }

  alternarSesion(donde: 'lateral' | 'barra'): void {
    this.menuSesion.update(actual => (actual === donde ? null : donde));
  }

  abrirBuscador(): void {
    window.dispatchEvent(new CustomEvent(ABRIR_BUSCADOR));
  }

  entrar(): void {
    this.auth.navigateToLogin();
  }

  salir(): void {
    this.menuSesion.set(null);
    this.auth.logout().subscribe();
  }

  /** El menú desplegado se abre con aria-expanded; Escape cierra lo que haya abierto. */
  @HostListener('document:keydown.escape')
  alPulsarEscape(): void {
    this.abiertoMovil.set(false);
    this.menuSesion.set(null);
  }

  @HostListener('document:click', ['$event'])
  alHacerClic(e: MouseEvent): void {
    const dentro = (e.target as HTMLElement | null)?.closest('.ig-sesion-zona');
    if (!dentro) {
      this.menuSesion.set(null);
    }
  }

  private leerPreferencia(): boolean {
    try {
      return localStorage.getItem(CLAVE_MENU_CERRADO) === '1';
    } catch {
      return false;
    }
  }
}
