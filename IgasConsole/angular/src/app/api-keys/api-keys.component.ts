import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChangeDetectorRef, Component, NgZone, OnInit, inject } from '@angular/core';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { ApiKeyDto, ApiKeysService } from './api-keys.service';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { CabeceraComponent } from '../shared/cabecera.component';
import { VacioComponent } from '../shared/vacio.component';

@Component({
  selector: 'app-api-keys',
  standalone: true,
  imports: [CommonModule, FormsModule, CoreModule, CabeceraComponent, EsqueletoComponent, VacioComponent],
  templateUrl: './api-keys.component.html',
  styleUrls: ['./api-keys.component.scss'],
})
export class ApiKeysComponent implements OnInit {
  private readonly textos = inject(LocalizationService);
  private readonly servicio = inject(ApiKeysService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  claves: ApiKeyDto[] = [];
  cargando = true;
  creando = false;
  error?: string;

  nombre = '';
  dias?: number;

  /**
   * La clave recién creada, en claro. Se muestra UNA vez y desaparece al recargar: el
   * servidor solo guarda el hash y no puede volver a enseñarla.
   */
  claveNueva?: string;
  copiada = false;

  /** Mismo helper que en las demás pantallas: aplicar el cambio y repintar siempre. */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.cargar();
  }

  private cargar(): void {
    this.servicio.getList().subscribe({
      next: filas =>
        this.aplicar(() => {
          this.claves = filas;
          this.cargando = false;
          this.error = undefined;
        }),
      error: e =>
        this.aplicar(() => {
          this.cargando = false;
          this.error =
            e?.status === 403
              ? 'No tienes permiso para gestionar claves de API.'
              : 'No se pudo consultar la lista de claves.';
        }),
    });
  }

  crear(): void {
    if (!this.nombre.trim()) {
      this.error = this.textos.instant('::ApiKeys:NeedName');
      return;
    }

    this.aplicar(() => {
      this.creando = true;
      this.error = undefined;
      this.claveNueva = undefined;
      this.copiada = false;
    });

    this.servicio.create(this.nombre.trim(), this.dias).subscribe({
      next: r =>
        this.aplicar(() => {
          this.creando = false;
          this.claveNueva = r.plainKey;
          this.nombre = '';
          this.dias = undefined;
          this.cargar();
        }),
      error: e =>
        this.aplicar(() => {
          this.creando = false;
          this.error = e?.error?.error?.message ?? 'No se pudo crear la clave.';
        }),
    });
  }

  async copiar(): Promise<void> {
    if (!this.claveNueva) {
      return;
    }

    try {
      await navigator.clipboard.writeText(this.claveNueva);
      this.aplicar(() => (this.copiada = true));
    } catch {
      // El portapapeles falla sin HTTPS o sin permiso. No es un error que merezca
      // interrumpir: la clave sigue en pantalla para seleccionarla a mano.
      this.aplicar(() => (this.copiada = false));
    }
  }

  revocar(clave: ApiKeyDto): void {
    this.servicio.revoke(clave.id).subscribe({
      next: () => this.aplicar(() => this.cargar()),
      error: () => this.aplicar(() => (this.error = this.textos.instant('::ApiKeys:RevokeError'))),
    });
  }

  fecha(iso?: string | null): string {
    return iso ? new Date(iso).toLocaleString() : '—';
  }
}
