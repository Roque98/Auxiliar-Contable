import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnDestroy, OnInit, inject } from '@angular/core';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { Subscription, finalize, interval } from 'rxjs';
import {
  StartTelegramLinkResultDto,
  TelegramLinkService,
  TelegramLinkStatusDto,
} from './telegram-link.service';
import { CabeceraComponent } from '../shared/cabecera.component';

@Component({
  selector: 'app-telegram-link',
  standalone: true,
  imports: [CommonModule, CoreModule, CabeceraComponent],
  templateUrl: './telegram-link.component.html',
  styleUrls: ['./telegram-link.component.scss'],
})
export class TelegramLinkComponent implements OnInit, OnDestroy {
  private readonly textos = inject(LocalizationService);
  private readonly servicio = inject(TelegramLinkService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);
  private sondeo?: Subscription;

  estado?: TelegramLinkStatusDto;
  enlace?: StartTelegramLinkResultDto;
  cargando = true;
  generando = false;
  error?: string;

  /**
   * Aplica un cambio de estado y REPINTA, pase lo que pase.
   * <para>Hace falta porque el sintoma observado solo lo explica esto: la respuesta HTTP
   * llegaba (200, con los datos correctos, visible en la captura de red) y la pantalla
   * seguia mostrando "Consultando…". Si el callback corre fuera de la zona de Angular
   * —cosa que puede provocar la pila de OAuth— no se dispara deteccion de cambios y la
   * vista se queda congelada en el estado anterior aunque los datos ya esten en memoria.</para>
   */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.cargarEstado();
  }

  ngOnDestroy(): void {
    this.sondeo?.unsubscribe();
  }

  private cargarEstado(): void {

    // TRY/CATCH alrededor de la construccion del observable, no solo del subscribe.
    // Si getStatus() lanza de forma SINCRONA (por ejemplo si la configuracion de la API
    // aun no esta cargada y RestService no puede resolver la URL), no llega a existir
    // ningun observable: entonces no se ejecuta ni error, ni finalize, ni nada, y el
    // indicador se queda encendido para siempre. Es el unico camino que explica ver
    // "Consultando…" con el servidor devolviendo 200.
    try {
      this.peticion();
    } catch (e) {
      this.cargando = false;
      this.error =
        'La petición falló antes de salir del navegador: ' + ((e as Error)?.message ?? e);
    }
  }

  /** Reintento manual, para no obligar a recargar la página entera. */
  reintentar(): void {
    this.cargando = true;
    this.error = undefined;
    this.cargarEstado();
  }

  private peticion(): void {
    // finalize y NO solo next/error: RestService con skipHandleError puede COMPLETAR sin
    // emitir cuando la petición falla, y entonces ni next ni error se ejecutan. Sin esto,
    // la pantalla se queda en "Consultando…" para siempre y sin decir por qué — que es
    // exactamente lo que pasó la primera vez que se desplegó esta página.
    this.servicio
      .getStatus()
      .pipe(
        finalize(() =>
          this.aplicar(() => {
            this.cargando = false;
          }),
        ),
      )
      .subscribe({
        next: e =>
          this.aplicar(() => {
            this.estado = e;
            this.error = undefined;
            // Si ya quedó vinculado, el enlace generado sobra.
            if (e.isLinked) {
              this.enlace = undefined;
              this.sondeo?.unsubscribe();
            }
          }),
        error: e =>
          this.aplicar(() => {
            // El motivo se muestra: un mensaje genérico aquí obliga a abrir la consola
            // del navegador para averiguar algo que el servidor ya dijo.
            this.error =
              'No se pudo consultar el estado de la vinculación. ' +
              (e?.status ? `El servidor respondió ${e.status}.` : 'No hubo respuesta del servidor.');
          }),
      });
  }

  generar(): void {
    this.generando = true;
    this.error = undefined;

    this.servicio.start().subscribe({
      next: r =>
        this.aplicar(() => {
          this.enlace = r;
          this.generando = false;

          // La confirmación ocurre FUERA de esta pantalla: el usuario habla con el bot y
          // es el servidor quien se entera. Por eso se consulta el estado cada pocos
          // segundos en vez de esperar una respuesta que nunca va a llegar por aquí.
          this.sondeo?.unsubscribe();
          this.sondeo = interval(3000).subscribe(() => this.cargarEstado());
        }),
      error: e =>
        this.aplicar(() => {
          this.generando = false;
          this.error = e?.error?.error?.message ?? 'No se pudo generar el enlace.';
        }),
    });
  }

  cambiandoSegundoFactor = false;

  alternarSegundoFactor(): void {
    if (!this.estado) {
      return;
    }

    const nuevo = !this.estado.twoFactorEnabled;

    this.aplicar(() => {
      this.cambiandoSegundoFactor = true;
      this.error = undefined;
    });

    this.servicio.setTwoFactor(nuevo).subscribe({
      next: e =>
        this.aplicar(() => {
          this.estado = e;
          this.cambiandoSegundoFactor = false;
        }),
      error: e =>
        this.aplicar(() => {
          this.cambiandoSegundoFactor = false;
          this.error =
            e?.error?.error?.message ?? 'No se pudo cambiar el segundo factor.';
        }),
    });
  }

  desvincular(): void {
    this.servicio.unlink().subscribe({
      next: () =>
        this.aplicar(() => {
          this.enlace = undefined;
          this.cargarEstado();
        }),
      error: () => this.aplicar(() => (this.error = this.textos.instant('::Telegram:UnlinkError'))),
    });
  }

  get vinculadoTexto(): string {
    const iso = this.estado?.linkedUtc;
    if (!iso) {
      return '';
    }
    return new Date(iso).toLocaleString();
  }
}
