import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { Subscription } from 'rxjs';
import { BrandingDto, BrandingService, COLOR_DE_SERIE } from './branding.service';
import { normalizarLogo } from './normalizar-logo';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { CabeceraComponent } from '../shared/cabecera.component';

/** Mismo valor que BrandingConsts.MaxLadoPixeles en el backend. */
const LADO_MAXIMO = 256;

/** Mismo valor que BrandingConsts.MaxAppNameLength. */
const MAX_NOMBRE = 64;

@Component({
  selector: 'app-branding',
  standalone: true,
  imports: [CommonModule, CoreModule, FormsModule, CabeceraComponent, EsqueletoComponent],
  templateUrl: './branding.component.html',
  styleUrls: ['./branding.component.scss'],
})
export class BrandingComponent implements OnInit, OnDestroy {
  private readonly textos = inject(LocalizationService);
  private readonly servicio = inject(BrandingService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  private readonly subs = new Subscription();

  readonly maxNombre = MAX_NOMBRE;
  readonly colorPorDefecto = COLOR_DE_SERIE;
  readonly ladoMaximoTexto = `${LADO_MAXIMO}×${LADO_MAXIMO}`;

  estado?: BrandingDto;
  /** URL absoluta del logo, o null. La misma que pinta la barra lateral. */
  logoUrl: string | null = null;
  cargando = true;
  guardando = false;
  subiendo = false;
  error?: string;
  aviso?: string;

  /** Lo que hay escrito en el formulario, que puede diferir de lo guardado. */
  nombre = '';
  color = COLOR_DE_SERIE;

  /** Ver CLAUDE.md §5.1. Aquí además hay callbacks de canvas, territorio clásico para eso. */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.cargar();
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  private cargar(): void {
    // El logo de la vista previa sale de `marca$` —la misma fuente que pinta la barra
    // lateral— y no del DTO: el `logoUrl` del DTO es una ruta RELATIVA a la API, que en
    // desarrollo vive en otro puerto, así que en un `<img src>` del SPA daría un 404.
    this.subs.add(
      this.servicio.marca$.subscribe(m => this.aplicar(() => (this.logoUrl = m.logoUrl))),
    );

    this.servicio.obtener().subscribe({
      next: dto =>
        this.aplicar(() => {
          this.recibirEstado(dto);
          this.rellenarFormulario(dto);
          this.cargando = false;
        }),
      error: e =>
        this.aplicar(() => {
          this.cargando = false;
          this.error = this.explicar(e, this.textos.instant('::Branding:LoadError'));
        }),
    });
  }

  /** Lo que siempre se refresca: el estado guardado en el servidor. */
  private recibirEstado(dto: BrandingDto): void {
    this.estado = dto;
    this.guardando = false;
    this.subiendo = false;
    this.error = undefined;
  }

  /**
   * Vuelca lo guardado en los campos del formulario.
   *
   * ⚠️ Solo al CARGAR y al GUARDAR, nunca tras subir o quitar el logo. Haciéndolo también
   * ahí, quien escribiera un nombre nuevo y después eligiera un logo veía cómo su texto
   * volvía en silencio al valor anterior — y al pulsar Guardar se guardaba ese, no el suyo.
   * Se detectó automatizando el navegador: el logo entraba y el nombre no, sin ningún error.
   *
   * El campo se rellena con lo PROPIO y no con lo efectivo: con el heredado dentro, guardar
   * sin tocar nada lo convertiría en propio y el inquilino dejaría de seguir a la
   * instalación sin haberlo pedido.
   */
  private rellenarFormulario(dto: BrandingDto): void {
    this.nombre = dto.appName ?? '';
    this.color = dto.accentColor;
  }

  guardar(): void {
    this.aplicar(() => {
      this.guardando = true;
      this.error = undefined;
      this.aviso = undefined;
    });

    this.servicio.guardar({ appName: this.nombre, accentColor: this.color }).subscribe({
      // Aquí sí se relee el formulario: el servidor normaliza (recorta espacios, pasa el
      // color a minúsculas), y enseñar lo guardado de verdad evita que la pantalla afirme
      // algo distinto de lo que hay.
      next: dto => this.tras(dto, true),
      error: e =>
        this.aplicar(() => {
          this.guardando = false;
          this.error = this.explicar(e, this.textos.instant('::Branding:SaveError'));
        }),
    });
  }

  async elegirLogo(evento: Event): Promise<void> {
    const input = evento.target as HTMLInputElement;
    const archivo = input.files?.[0];

    // Se limpia SIEMPRE: si no, elegir dos veces el mismo archivo no dispara 'change' la
    // segunda vez y parece que el botón se ha quedado muerto.
    input.value = '';

    if (!archivo) {
      return;
    }

    this.aplicar(() => {
      this.subiendo = true;
      this.error = undefined;
      this.aviso = undefined;
    });

    let imagen: Blob;
    try {
      imagen = await normalizarLogo(archivo, LADO_MAXIMO);
    } catch {
      // Aquí se llega cuando el navegador no sabe decodificar el archivo: un PDF
      // renombrado, un SVG en algunos navegadores, un HEIC...
      this.aplicar(() => {
        this.subiendo = false;
        this.error = this.textos.instant('::Branding:NotAnImage');
      });
      return;
    }

    this.servicio.subirLogo(imagen, 'logo.png').subscribe({
      next: dto => this.tras(dto),
      error: e =>
        this.aplicar(() => {
          this.subiendo = false;
          this.error = this.explicar(e, this.textos.instant('::Branding:LogoError'));
        }),
    });
  }

  quitarLogo(): void {
    this.aplicar(() => {
      this.subiendo = true;
      this.error = undefined;
      this.aviso = undefined;
    });

    this.servicio.borrarLogo().subscribe({
      next: dto => this.tras(dto),
      error: e =>
        this.aplicar(() => {
          this.subiendo = false;
          this.error = this.explicar(e, this.textos.instant('::Branding:RemoveError'));
        }),
    });
  }

  restablecerColor(): void {
    this.color = COLOR_DE_SERIE;
  }

  /**
   * Tras cualquier cambio hay que volver a pedir la configuración de ABP: los ajustes de
   * marca viajan dentro de ella, y sin refrescarla la barra lateral y la pestaña seguirían
   * con el nombre y el logo anteriores hasta recargar la página a mano.
   */
  private tras(dto: BrandingDto, rellenar = false): void {
    const terminar = () =>
      this.aplicar(() => {
        this.recibirEstado(dto);
        if (rellenar) {
          this.rellenarFormulario(dto);
        }
        this.aviso = this.textos.instant('::Branding:Saved');
      });

    this.servicio.refrescarConfiguracion().subscribe({
      next: terminar,
      // Que falle el refresco no invalida lo guardado: se avisa igual y lo que queda
      // desactualizado es solo la barra lateral, hasta la siguiente carga.
      error: terminar,
    });
  }

  /** El nombre que se vería si se guardara lo que hay escrito ahora mismo. */
  get nombreEnVistaPrevia(): string {
    const escrito = this.nombre.trim();
    return escrito || this.heredado;
  }

  /** El que se heredaría al dejar el campo vacío. */
  get heredado(): string {
    return this.estado?.inheritedAppName ?? '';
  }

  private explicar(e: any, porDefecto: string): string {
    // Se prefiere el mensaje del servidor: ya explica el caso concreto ("el color tiene que
    // ir en formato #RRGGBB"), y un texto genérico obligaría a abrir la consola.
    return e?.error?.error?.message ?? porDefecto;
  }
}
