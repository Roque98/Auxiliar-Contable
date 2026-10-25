import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnDestroy, OnInit, inject } from '@angular/core';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { Subscription, interval } from 'rxjs';
import { SystemStatusDto, SystemStatusService } from './system-status.service';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { CabeceraComponent } from '../shared/cabecera.component';

@Component({
  selector: 'app-system-status',
  standalone: true,
  imports: [CommonModule, CoreModule, CabeceraComponent, EsqueletoComponent],
  templateUrl: './system-status.component.html',
  styleUrls: ['./system-status.component.scss'],
})
export class SystemStatusComponent implements OnInit, OnDestroy {
  private readonly textos = inject(LocalizationService);
  private readonly servicio = inject(SystemStatusService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  /** Un minuto: es el ritmo al que el recolector produce datos nuevos. Más rápido sería
      pedir lo mismo dos veces. */
  private static readonly RefrescoMs = 60_000;

  private readonly subs = new Subscription();

  estado?: SystemStatusDto;
  cargando = true;
  error?: string;
  actualizado?: Date;

  /** Ver CLAUDE.md §5.1: la pila de OAuth puede dejar el callback fuera de la zona. */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.cargar();
    this.subs.add(interval(SystemStatusComponent.RefrescoMs).subscribe(() => this.cargar()));
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }

  cargar(): void {
    this.servicio.get().subscribe({
      next: e =>
        this.aplicar(() => {
          this.estado = e;
          this.cargando = false;
          this.error = undefined;
          this.actualizado = new Date();
        }),
      // Un sondeo perdido no puede vaciar la pantalla: se conserva lo último bueno y se
      // reintenta al minuto. Ver la misma decisión en /metrics.
      error: () =>
        this.aplicar(() => {
          this.cargando = false;
          if (!this.estado) {
            this.error = this.textos.instant('::SystemStatus:LoadError');
          }
        }),
    });
  }

  /** Verde solo cuando de verdad está bien; el resto se distingue entre aviso y avería. */
  claseEstado(estado: string): string {
    switch (estado) {
      case 'Ok':
      case 'Healthy':
        return 'bg-success';
      case 'Fallando':
      case 'Unhealthy':
        return 'bg-danger';
      case 'SinInformacion':
        return 'bg-secondary';
      default:
        return 'bg-warning text-dark';
    }
  }

  /** Un número distinto de cero aquí es lo que hay que mirar; cero es fondo. */
  claseContador(valor: number, peligroso = true): string {
    if (valor === 0) {
      return 'bg-secondary';
    }
    return peligroso ? 'bg-danger' : 'bg-warning text-dark';
  }

  textoEstado(estado: string): string {
    switch (estado) {
      case 'Ok':
        return 'Correcto';
      case 'Fallando':
        return 'Fallando';
      case 'Caducada':
        return 'Caducada';
      case 'SoloLocal':
        return 'Solo local';
      case 'SinInformacion':
        return 'Sin datos';
      default:
        return estado;
    }
  }

  tamano(bytes: number): string {
    if (!bytes) {
      return '—';
    }
    const mb = bytes / (1024 * 1024);
    return mb >= 1 ? `${mb.toFixed(1)} MB` : `${(bytes / 1024).toFixed(0)} KB`;
  }

  enMarcha(segundos: number): string {
    if (!segundos) {
      return '—';
    }
    const d = Math.floor(segundos / 86400);
    const h = Math.floor((segundos % 86400) / 3600);
    const m = Math.floor((segundos % 3600) / 60);
    if (d > 0) return `${d} d ${h} h`;
    if (h > 0) return `${h} h ${m} min`;
    return `${m} min`;
  }

  fecha(iso?: string | null): string {
    return iso
      ? new Date(iso).toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' })
      : '—';
  }

  /** ¿Hay algo que reclame atención ahora mismo? Decide el mensaje de cabecera. */
  get todoEnOrden(): boolean {
    const e = this.estado;
    if (!e) return false;
    return (
      e.backup.state === 'Ok' &&
      e.restoreCheck.state !== 'Fallando' &&
      e.abandonedJobs === 0 &&
      e.health === 'Healthy'
    );
  }
}
