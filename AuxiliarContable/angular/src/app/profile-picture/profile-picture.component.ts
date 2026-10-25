import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnDestroy, OnInit, inject } from '@angular/core';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { Subscription } from 'rxjs';
import { ProfilePictureService } from './profile-picture.service';
import { normalizarImagen } from './normalizar-imagen';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { CabeceraComponent } from '../shared/cabecera.component';

/** Mismo valor que ProfilePictureConsts.MaxLadoPixeles en el backend. */
const LADO_MAXIMO = 512;

@Component({
  selector: 'app-profile-picture',
  standalone: true,
  imports: [CommonModule, CoreModule, CabeceraComponent, EsqueletoComponent],
  templateUrl: './profile-picture.component.html',
  styleUrls: ['./profile-picture.component.scss'],
})
export class ProfilePictureComponent implements OnInit, OnDestroy {
  private readonly textos = inject(LocalizationService);
  private readonly servicio = inject(ProfilePictureService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);
  private suscripcion?: Subscription;

  readonly ladoMaximoTexto = `${LADO_MAXIMO}×${LADO_MAXIMO}`;

  foto: string | null = null;
  cargando = true;
  subiendo = false;

  /** El texto del botón, con sus tres estados. Ver el comentario de la plantilla. */
  get textoDelBoton(): string {
    if (this.subiendo) {
      return this.textos.instant('::ProfilePicture:Guardando');
    }
    return this.textos.instant(this.foto ? '::ProfilePicture:Cambiar' : '::ProfilePicture:Elegir');
  }
  error?: string;
  aviso?: string;

  /**
   * Aplica un cambio de estado y REPINTA, pase lo que pase.
   * <para>Mismo motivo que en la pantalla de Telegram: si el callback corre fuera de la
   * zona de Angular, los datos se actualizan en memoria y la vista se queda congelada.
   * Aquí además hay callbacks de canvas y de FileReader, que son territorio clásico para
   * eso.</para>
   */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.suscripcion = this.servicio.foto$.subscribe(foto =>
      this.aplicar(() => {
        this.foto = foto;
      }),
    );

    this.servicio.cargar().subscribe({
      next: () => this.aplicar(() => (this.cargando = false)),
      error: e =>
        this.aplicar(() => {
          this.cargando = false;
          this.error = this.explicar(e, 'No se pudo consultar tu foto de perfil.');
        }),
    });
  }

  ngOnDestroy(): void {
    this.suscripcion?.unsubscribe();
  }

  async elegir(evento: Event): Promise<void> {
    const input = evento.target as HTMLInputElement;
    const archivo = input.files?.[0];

    // Se limpia el input SIEMPRE: si no, elegir dos veces el mismo archivo no dispara
    // 'change' la segunda vez y parece que el botón se ha quedado muerto.
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
      imagen = await normalizarImagen(archivo, LADO_MAXIMO);
    } catch {
      // Llega aquí cuando el archivo no es una imagen que el navegador sepa decodificar
      // (un PDF renombrado, un HEIC en un navegador que no lo soporta...).
      this.aplicar(() => {
        this.subiendo = false;
        this.error =
          'No se pudo leer ese archivo como imagen. Prueba con un JPG o un PNG.';
      });
      return;
    }

    this.servicio.subir(imagen, 'perfil.jpg').subscribe({
      next: () =>
        this.aplicar(() => {
          this.subiendo = false;
          this.aviso = this.textos.instant('::ProfilePicture:Updated');
        }),
      error: e =>
        this.aplicar(() => {
          this.subiendo = false;
          this.error = this.explicar(e, 'No se pudo guardar la foto.');
        }),
    });
  }

  quitar(): void {
    this.aplicar(() => {
      this.error = undefined;
      this.aviso = undefined;
    });

    this.servicio.borrar().subscribe({
      next: () => this.aplicar(() => (this.aviso = this.textos.instant('::ProfilePicture:Removed'))),
      error: e => this.aplicar(() => (this.error = this.explicar(e, 'No se pudo quitar la foto.'))),
    });
  }

  /**
   * Prefiere el mensaje que ya mandó el servidor. Un texto genérico obligaría a abrir la
   * consola del navegador para averiguar algo que la API ya explicó ("la imagen pesa más
   * de 1024 KB", por ejemplo).
   */
  private explicar(e: any, porDefecto: string): string {
    return e?.error?.error?.message ?? porDefecto;
  }
}
