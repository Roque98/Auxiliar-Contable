import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnInit, inject } from '@angular/core';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { Router } from '@angular/router';
import { NotificationDto, NotificationsService } from './notifications.service';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { CabeceraComponent } from '../shared/cabecera.component';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule, CoreModule, CabeceraComponent, EsqueletoComponent],
  templateUrl: './notifications.component.html',
  styleUrls: ['./notifications.component.scss'],
})
export class NotificationsComponent implements OnInit {
  private readonly textos = inject(LocalizationService);
  private readonly servicio = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  avisos: NotificationDto[] = [];
  soloSinLeer = false;
  cargando = true;
  error?: string;

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
    this.servicio.getList(this.soloSinLeer, 100).subscribe({
      next: filas =>
        this.aplicar(() => {
          this.avisos = filas;
          this.cargando = false;
          this.error = undefined;
        }),
      error: () =>
        this.aplicar(() => {
          this.cargando = false;
          this.error = this.textos.instant('::Notifications:LoadError');
        }),
    });
  }

  filtrar(soloSinLeer: boolean): void {
    this.soloSinLeer = soloSinLeer;
    this.cargando = true;
    this.cargar();
  }

  abrir(aviso: NotificationDto): void {
    const irA = () => {
      if (aviso.url) {
        this.router.navigateByUrl(aviso.url);
      }
    };

    if (aviso.isRead) {
      irA();
      return;
    }

    this.servicio.markRead([aviso.id]).subscribe({
      next: () => {
        this.servicio.refrescarContador().subscribe({ error: () => undefined });
        irA();
      },
      error: () => irA(),
    });
  }

  marcarLeido(aviso: NotificationDto, evento: MouseEvent): void {
    // Marcar leído sin navegar: para lo que ya se ha atendido por otro lado.
    evento.stopPropagation();

    this.servicio.markRead([aviso.id]).subscribe({
      next: () => {
        this.servicio.refrescarContador().subscribe({ error: () => undefined });
        this.cargar();
      },
      error: () => this.aplicar(() => (this.error = this.textos.instant('::Notifications:MarkError'))),
    });
  }

  marcarTodo(): void {
    this.servicio.markAllRead().subscribe({
      next: () => {
        this.servicio.refrescarContador().subscribe({ error: () => undefined });
        this.cargar();
      },
      error: () => this.aplicar(() => (this.error = this.textos.instant('::Notifications:MarkAllError'))),
    });
  }

  get haySinLeer(): boolean {
    return this.avisos.some(a => !a.isRead);
  }

  fecha(iso: string): string {
    return new Date(iso).toLocaleString();
  }

  claseSeveridad(severidad: string): string {
    switch (severidad) {
      case 'Error':
        return 'bg-danger';
      case 'Warning':
        return 'bg-warning text-dark';
      default:
        return 'bg-primary';
    }
  }

  textoSeveridad(severidad: string): string {
    switch (severidad) {
      case 'Error':
        return 'Error';
      case 'Warning':
        return 'Aviso';
      default:
        return 'Info';
    }
  }
}
