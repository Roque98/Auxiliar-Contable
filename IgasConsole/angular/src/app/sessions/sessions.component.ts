import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnInit, inject } from '@angular/core';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { SessionsService, UserSessionDto } from './sessions.service';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { CabeceraComponent } from '../shared/cabecera.component';
import { VacioComponent } from '../shared/vacio.component';

@Component({
  selector: 'app-sessions',
  standalone: true,
  imports: [CommonModule, CoreModule, CabeceraComponent, EsqueletoComponent, VacioComponent],
  templateUrl: './sessions.component.html',
  styleUrls: ['./sessions.component.scss'],
})
export class SessionsComponent implements OnInit {
  private readonly textos = inject(LocalizationService);
  private readonly servicio = inject(SessionsService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  sesiones: UserSessionDto[] = [];

  // --- Paginado ---------------------------------------------------------------
  //
  // ⚠️ Medido: esta pantalla llegaba a 9.632 px — casi 11 pantallas de scroll — con 141
  // filas y 144 controles. Y no era artificio de las pruebas: se crea UNA sesion por login
  // y la purga es a 30 dias, asi que alguien que entra a diario desde tres dispositivos
  // acumula decenas. Una lista asi deja de servir para lo unico que se le pide: reconocer
  // de un vistazo lo que NO deberias ver ahi.
  //
  // El paginado es en el CLIENTE y no en el servidor a proposito: son las sesiones de UNA
  // persona, unos kilobytes, y ya vienen ordenadas con la actual primero. Pedirlas por
  // paginas obligaria a mantener ese orden en dos sitios sin ganar nada.
  pagina = 0;
  readonly porPagina = 15;

  get paginaDeSesiones(): UserSessionDto[] {
    return this.sesiones.slice(this.pagina * this.porPagina, (this.pagina + 1) * this.porPagina);
  }

  get hayMasPaginas(): boolean {
    return (this.pagina + 1) * this.porPagina < this.sesiones.length;
  }

  anterior(): void {
    if (this.pagina > 0) { this.pagina--; }
  }

  siguiente(): void {
    if (this.hayMasPaginas) { this.pagina++; }
  }
  cargando = true;
  error?: string;

  /** Mismo helper que en el resto de pantallas: aplicar el cambio y repintar siempre. */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.cargar();
  }

  cargar(): void {
    this.servicio.getList().subscribe({
      next: filas =>
        this.aplicar(() => {
          this.sesiones = filas;
          this.cargando = false;
          this.error = undefined;
        }),
      error: () =>
        this.aplicar(() => {
          this.cargando = false;
          this.error = this.textos.instant('::Sessions:LoadError');
        }),
    });
  }

  cerrar(sesion: UserSessionDto): void {
    this.servicio.revoke(sesion.id).subscribe({
      next: () =>
        this.aplicar(() => {
          if (sesion.isCurrent) {
            // Cerrar la propia es echarse a uno mismo. Se recarga para que la aplicación
            // vuelva al estado de "sin sesión" en vez de quedarse con una pantalla que ya
            // no tiene permiso para nada.
            window.location.href = '/';
            return;
          }
          this.cargar();
        }),
      error: () => this.aplicar(() => (this.error = this.textos.instant('::Sessions:CloseError'))),
    });
  }

  cerrarLasDemas(): void {
    this.servicio.revokeOthers().subscribe({
      next: () => this.aplicar(() => this.cargar()),
      error: e =>
        this.aplicar(() => {
          this.error = e?.error?.error?.message ?? 'No se pudieron cerrar las demás sesiones.';
        }),
    });
  }

  fecha(iso?: string | null): string {
    return iso ? new Date(iso).toLocaleString() : '—';
  }

  get hayOtras(): boolean {
    return this.sesiones.some(s => !s.isCurrent);
  }
}
