import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnInit, inject } from '@angular/core';
import { CoreModule, LocalizationService, PermissionService } from '@abp/ng.core';
import { JobDto, JobFailureDto, JobSummaryDto, JobsService } from './jobs.service';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { CabeceraComponent } from '../shared/cabecera.component';
import { VacioComponent } from '../shared/vacio.component';

@Component({
  selector: 'app-jobs',
  standalone: true,
  imports: [CommonModule, CoreModule, CabeceraComponent, EsqueletoComponent, VacioComponent],
  templateUrl: './jobs.component.html',
  styleUrls: ['./jobs.component.scss'],
})
export class JobsComponent implements OnInit {
  private readonly servicio = inject(JobsService);
  private readonly permisos = inject(PermissionService);
  private readonly textos = inject(LocalizationService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly estados = ['Esperando', 'Reintentando', 'Abandonado', 'Completado'];

  trabajos: JobDto[] = [];
  resumen?: JobSummaryDto;
  filtro: string | null = null;
  cargando = true;
  error?: string;

  /** Id del trabajo cuyo detalle está abierto. Solo uno a la vez. */
  abierto?: string;
  historial: JobFailureDto[] = [];

  /**
   * El ESTADO viene del servidor con nombres internos (Esperando, Reintentando…). Se
   * traduce aqui y no en el servidor a proposito: el nombre interno es parte del contrato
   * de la API —el filtro se manda con el— y traducirlo alli haria que la respuesta
   * dependiera del idioma de quien pregunta.
   */
  etiquetaEstado(estado: string): string {
    return this.textos.instant('::Jobs:State:' + estado);
  }

  /** Los ternarios de la plantilla no admiten el pipe: se resuelven aqui. */
  readonly textoOcultar = this.textos.instant('::Common:Hide');
  readonly textoDetalle = this.textos.instant('::Common:Detail');

  /** Sin el permiso hijo la pantalla se ve pero no se toca. */
  readonly puedeGestionar = this.permisos.getGrantedPolicy('IgasConsole.Jobs.Manage');

  /** Mismo helper que el resto de pantallas: aplicar el cambio y repintar siempre. */
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
    this.servicio.getSummary().subscribe({
      next: r => this.aplicar(() => (this.resumen = r)),
      // El resumen es un adorno: si falla, la lista sigue valiendo.
      error: () => this.aplicar(() => (this.resumen = undefined)),
    });

    this.servicio.getList(this.filtro, 100).subscribe({
      next: filas =>
        this.aplicar(() => {
          this.trabajos = filas;
          this.cargando = false;
          this.error = undefined;
        }),
      error: () =>
        this.aplicar(() => {
          this.cargando = false;
          this.error = this.textos.instant('::Jobs:LoadError');
        }),
    });
  }

  filtrar(estado: string | null): void {
    this.filtro = estado;
    this.abierto = undefined;
    this.cargando = true;
    this.cargar();
  }

  alternarDetalle(trabajo: JobDto): void {
    if (this.abierto === trabajo.id) {
      this.aplicar(() => {
        this.abierto = undefined;
        this.historial = [];
      });
      return;
    }

    this.aplicar(() => {
      this.abierto = trabajo.id;
      this.historial = [];
    });

    this.servicio.getFailureHistory(trabajo.id).subscribe({
      next: h => this.aplicar(() => (this.historial = h)),
      error: () => this.aplicar(() => (this.historial = [])),
    });
  }

  reintentar(trabajo: JobDto): void {
    this.servicio.retry(trabajo.id).subscribe({
      next: () => this.aplicar(() => this.cargar()),
      error: e =>
        this.aplicar(() => {
          this.error = e?.error?.error?.message ?? this.textos.instant('::Jobs:RetryError');
        }),
    });
  }

  borrar(trabajo: JobDto): void {
    // Sacar algo de la cola no se deshace, y el mensaje dice qué se pierde y qué no.
    // El nombre del trabajo va como parametro {0}: en otro idioma la frase puede
    // necesitarlo en otra posicion.
    const seguro = window.confirm(
      this.textos.instant('::Jobs:ConfirmDelete', trabajo.jobName),
    );
    if (!seguro) {
      return;
    }

    this.servicio.delete(trabajo.id).subscribe({
      next: () => this.aplicar(() => this.cargar()),
      error: () => this.aplicar(() => (this.error = this.textos.instant('::Jobs:DeleteError'))),
    });
  }

  /** Completa, con segundos. Para el detalle, donde se comparan intentos entre sí. */
  fecha(iso?: string | null): string {
    return iso ? new Date(iso).toLocaleString() : '—';
  }

  /**
   * Corta, para la tabla.
   * <para>No es cosmética: la fecha larga en una celda que no parte líneas ensancha la
   * tabla lo justo para que la página empiece a desplazarse de lado en escritorio. Sin
   * segundos cabe, y en un listado los segundos no dicen nada.</para>
   */
  fechaCorta(iso?: string | null): string {
    if (!iso) {
      return '—';
    }
    return new Date(iso).toLocaleString(undefined, {
      dateStyle: 'short',
      timeStyle: 'short',
    });
  }

  /** Color de la etiqueta de estado. Abandonado en rojo: es lo único que pide acción. */
  claseEstado(estado: string): string {
    switch (estado) {
      case 'Abandonado':
        return 'bg-danger';
      case 'Reintentando':
        return 'bg-warning text-dark';
      case 'Completado':
        return 'bg-success';
      default:
        return 'bg-secondary';
    }
  }

  /** Reintentar algo que ya salió bien lo ejecutaría otra vez: el servidor lo rechaza. */
  sePuedeReintentar(trabajo: JobDto): boolean {
    return this.puedeGestionar && trabajo.state !== 'Completado';
  }

  /** JSON legible. Si no parsea se enseña crudo: es un dato de diagnóstico. */
  argumentos(json: string): string {
    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }
}
