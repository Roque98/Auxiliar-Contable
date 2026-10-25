import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChangeDetectorRef, Component, NgZone, OnInit, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { PermissionService } from '@abp/ng.core';
import { BlockedIpDto, BlockedIpsService } from './security.service';
import { MetricsService, RateLimitEventDto } from '../metrics/metrics.service';
import { descargarBlob, nombreConFecha } from '../metrics/descargar';
import { SecurityLogDto, SecurityLogsService } from './security-logs.service';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { CabeceraComponent } from '../shared/cabecera.component';
import { VacioComponent } from '../shared/vacio.component';

@Component({
  selector: 'app-security',
  standalone: true,
  imports: [CommonModule, FormsModule, CoreModule, RouterLink, CabeceraComponent, EsqueletoComponent, VacioComponent],
  templateUrl: './security.component.html',
  styleUrls: ['./security.component.scss'],
})
export class SecurityComponent implements OnInit {
  private readonly textos = inject(LocalizationService);
  private readonly servicio = inject(BlockedIpsService);
  private readonly registros = inject(SecurityLogsService);
  private readonly metricas = inject(MetricsService);
  private readonly permisos = inject(PermissionService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  ips: BlockedIpDto[] = [];
  cargando = true;
  error?: string;

  nuevaIp = '';
  nuevoMotivo = '';
  nuevasHoras?: number;
  bloqueando = false;

  // --- Registro de seguridad ---------------------------------------------------
  /** Permiso propio: ver quién intentó entrar es lo más sensible del panel. */
  readonly puedeVerRegistro = this.permisos.getGrantedPolicy('AuxiliarContable.Security.Logs');
  registro: SecurityLogDto[] = [];
  totalRegistro = 0;
  accionesDisponibles: string[] = [];
  filtroDias = 7;
  filtroAccion = '';
  filtroUsuario = '';
  filtroIp = '';
  paginaRegistro = 0;
  readonly porPagina = 20;
  cargandoRegistro = false;

  /** Mismo helper que en el resto de pantallas: aplicar el cambio y repintar siempre. */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  // --- Trafico bloqueado por el rate limiter ---------------------------------
  //
  // ⚠️ Venia de /metrics. Aqui es donde pertenece: esta pantalla ya es la de «quien esta
  // llamando a la puerta» —bloqueo de IPs y registro de seguridad— y cada fila de esta tabla
  // enlaza justo con bloquear esa IP. En metricas competia con los graficos de CPU.

  bloqueados: RateLimitEventDto[] = [];
  horas = 24;
  exportandoBloqueados = false;
  errorExport?: string;

  private cargarBloqueados(): void {
    this.metricas.getBlockedEvents(this.horas).subscribe({
      next: eventos => this.aplicar(() => (this.bloqueados = eventos)),
      // Sin permiso o sin datos, la tabla no se pinta: su *ngIf exige que haya filas.
      error: () => this.aplicar(() => (this.bloqueados = [])),
    });
  }

  /** Descarga los eventos con la MISMA ventana que se esta viendo. */
  exportarBloqueados(): void {
    this.exportandoBloqueados = true;
    this.errorExport = undefined;

    this.metricas.exportBlockedEvents(this.horas).subscribe({
      next: blob => this.aplicar(() => {
        descargarBlob(blob, nombreConFecha('trafico-bloqueado'));
        this.exportandoBloqueados = false;
      }),
      error: () => this.aplicar(() => {
        this.exportandoBloqueados = false;
        this.errorExport = this.textos.instant('::Common:DownloadError');
      }),
    });
  }

  /** Hora corta: la ventana de esta tabla es de 24 h, asi que el dia se sobreentiende. */
  hora(iso: string): string {
    return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  ngOnInit(): void {
    this.cargarBloqueados();

    // Se llega aquí desde la tabla de tráfico bloqueado con ?ip=…, para no copiarla a mano.
    const ip = this.ruta.snapshot.queryParamMap.get('ip');
    if (ip) {
      this.nuevaIp = ip;
      this.nuevoMotivo = 'Exceso de peticiones';
    }

    this.cargar();

    if (this.puedeVerRegistro) {
      // Las acciones del desplegable salen de los datos, no de una lista fija.
      this.registros.getFilters().subscribe({
        next: f => this.aplicar(() => (this.accionesDisponibles = f.actions)),
        error: () => undefined,
      });
      this.cargarRegistro();
    }
  }

  cargarRegistro(): void {
    this.cargandoRegistro = true;
    this.registros
      .getList({
        days: this.filtroDias,
        action: this.filtroAccion || undefined,
        userName: this.filtroUsuario || undefined,
        clientIpAddress: this.filtroIp || undefined,
        skipCount: this.paginaRegistro * this.porPagina,
        maxResultCount: this.porPagina,
      })
      .subscribe({
        next: r =>
          this.aplicar(() => {
            this.registro = r.items;
            this.totalRegistro = r.totalCount;
            this.cargandoRegistro = false;
          }),
        error: () =>
          this.aplicar(() => {
            this.registro = [];
            this.cargandoRegistro = false;
          }),
      });
  }

  filtrarRegistro(): void {
    // Cualquier cambio de filtro vuelve a la primera pagina: quedarse en la pagina 3 de un
    // filtro nuevo ensena una lista vacia que parece "no hay resultados".
    this.paginaRegistro = 0;
    this.cargarRegistro();
  }

  paginaAnterior(): void {
    if (this.paginaRegistro > 0) {
      this.paginaRegistro--;
      this.cargarRegistro();
    }
  }

  paginaSiguiente(): void {
    if ((this.paginaRegistro + 1) * this.porPagina < this.totalRegistro) {
      this.paginaRegistro++;
      this.cargarRegistro();
    }
  }

  /** Rellena el formulario de bloqueo con la IP de una fila del registro. */
  bloquearDesdeRegistro(fila: SecurityLogDto): void {
    if (!fila.clientIpAddress) {
      return;
    }
    this.nuevaIp = fila.clientIpAddress;
    this.nuevoMotivo = `Intentos de acceso (${fila.action})`;
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  get hayPaginaSiguiente(): boolean {
    return (this.paginaRegistro + 1) * this.porPagina < this.totalRegistro;
  }

  cargar(): void {
    this.servicio.getList().subscribe({
      next: filas =>
        this.aplicar(() => {
          this.ips = filas;
          this.cargando = false;
          this.error = undefined;
        }),
      error: e =>
        this.aplicar(() => {
          this.cargando = false;
          this.error =
            e?.status === 403
              ? 'No tienes permiso para ver las IPs bloqueadas.'
              : 'No se pudo consultar la lista.';
        }),
    });
  }

  bloquear(): void {
    if (!this.nuevaIp.trim() || !this.nuevoMotivo.trim()) {
      this.aplicar(() => (this.error = this.textos.instant('::Security:NeedIpAndReason')));
      return;
    }

    this.aplicar(() => {
      this.bloqueando = true;
      this.error = undefined;
    });

    this.servicio.block(this.nuevaIp.trim(), this.nuevoMotivo.trim(), this.nuevasHoras).subscribe({
      next: () =>
        this.aplicar(() => {
          this.bloqueando = false;
          this.nuevaIp = '';
          this.nuevoMotivo = '';
          this.nuevasHoras = undefined;
          this.cargar();
        }),
      error: e =>
        this.aplicar(() => {
          this.bloqueando = false;
          // El servidor ya explica el motivo ("esa es tu propia dirección", "ya está en la
          // lista"): un texto genérico obligaría a abrir la consola para saberlo.
          this.error = e?.error?.error?.message ?? 'No se pudo bloquear la IP.';
        }),
    });
  }

  desbloquear(fila: BlockedIpDto): void {
    this.servicio.unblock(fila.id).subscribe({
      next: () => this.aplicar(() => this.cargar()),
      error: () => this.aplicar(() => (this.error = this.textos.instant('::Security:UnblockError'))),
    });
  }

  fecha(iso?: string | null): string {
    return iso ? new Date(iso).toLocaleString() : '—';
  }

  get automaticas(): number {
    return this.ips.filter(x => x.isAutomatic && x.isActive).length;
  }
}
