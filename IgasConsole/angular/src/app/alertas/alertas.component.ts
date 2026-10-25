import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { CabeceraComponent } from '../shared/cabecera.component';
import { MonitoringSettingsDto, MonitoringSettingsService } from '../metrics/metrics.service';

/**
 * A dónde van los avisos del sistema: el bot de Telegram y los chats que los reciben.
 *
 * <p>⚠️ <b>Esto vivía dentro de `/metrics`, entre los gráficos, y era lo que más chirriaba
 * de aquella pantalla.</b> Un formulario para pegar el token de un bot no es una métrica: se
 * toca una vez al montar la instalación y no se vuelve a mirar. Estar ahí obligaba a bajar
 * por doce pantallas de gráficos y tablas de auditoría para llegar a un ajuste.</p>
 *
 * <p>El permiso sigue siendo <c>IgasConsole.Metrics.ManageSettings</c> y no uno nuevo: quien
 * puede cambiar a dónde van los avisos de toda la instalación es el mismo de antes, y un
 * permiso paralelo daría dos respuestas a la misma pregunta.</p>
 */
@Component({
  selector: 'app-alertas',
  standalone: true,
  imports: [CommonModule, FormsModule, CoreModule, CabeceraComponent],
  templateUrl: './alertas.component.html',
  styleUrls: ['./alertas.component.scss'],
})
export class AlertasComponent implements OnInit {
  private readonly textos = inject(LocalizationService);
  private readonly servicioAjustes = inject(MonitoringSettingsService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  ajustes?: MonitoringSettingsDto;
  error?: string;

  panelAjustesAbierto = true;
  guardandoAjustes = false;
  probando = false;
  mensajeAjustes?: { texto: string; ok: boolean };
  tokenNuevo = '';
  chatIdsEditados = '';
  avisosActivos = false;

  /** §5.1: cada callback que cambie algo que se pinte va dentro de la zona. */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.servicioAjustes.get().subscribe({
      next: a =>
        this.aplicar(() => {
          this.ajustes = a;
          this.chatIdsEditados = a.chatIds ?? '';
          this.avisosActivos = a.alertsEnabled;
        }),
      // Sin el permiso ManageSettings la llamada da 403: la pantalla se queda vacía, que no
      // es un error que haya que enseñarle a nadie — el menú ya la esconde.
      error: () => this.aplicar(() => (this.ajustes = undefined)),
    });
  }

  /**
   * ⚠️ El interruptor se guarda SOLO, en cuanto se acciona, y no viaja con el resto del
   * formulario.
   *
   * <p>Antes iba junto al token: guardar para cambiar el token enviaba también el interruptor
   * con el valor que tuviera cargado la página, y si esa página llevaba un rato abierta ese
   * valor estaba obsoleto y <b>apagaba los avisos</b>. Pasó en producción y no se notó,
   * porque unos avisos apagados se ven igual que unos que no tienen nada que decir.</p>
   */
  cambiarAvisos(activos: boolean): void {
    const anterior = this.avisosActivos;
    this.avisosActivos = activos;
    this.mensajeAjustes = undefined;

    this.servicioAjustes.update({ alertsEnabled: activos }).subscribe({
      next: a =>
        this.aplicar(() => {
          this.ajustes = a;
          this.avisosActivos = a.alertsEnabled;
          this.mensajeAjustes = {
            texto: this.textos.instant(activos ? '::Alertas:Activados' : '::Alertas:Desactivados'),
            ok: true,
          };
        }),
      error: e =>
        this.aplicar(() => {
          // Si no se pudo guardar, el interruptor vuelve a donde estaba: dejarlo movido haría
          // creer que el cambio se aplicó.
          this.avisosActivos = anterior;
          this.mensajeAjustes = {
            texto: e?.error?.error?.message ?? this.textos.instant('::Alertas:ErrorEstado'),
            ok: false,
          };
        }),
    });
  }

  guardarAjustes(): void {
    this.guardandoAjustes = true;
    this.mensajeAjustes = undefined;

    this.servicioAjustes
      .update({
        // alertsEnabled NO se manda: lo gestiona cambiarAvisos() por su cuenta.
        botToken: this.tokenNuevo.trim() || null,
        chatIds: this.chatIdsEditados.trim() || null,
      })
      .subscribe({
        next: a =>
          this.aplicar(() => {
            this.ajustes = a;
            // El token se limpia del formulario: de la respuesta solo vuelve una pista, así
            // que dejarlo escrito haría creer que se puede volver a leer.
            this.tokenNuevo = '';
            this.guardandoAjustes = false;
            this.mensajeAjustes = { texto: this.textos.instant('::Alertas:Guardado'), ok: true };
          }),
        error: e =>
          this.aplicar(() => {
            this.guardandoAjustes = false;
            this.mensajeAjustes = {
              texto: e?.error?.error?.message ?? this.textos.instant('::Alertas:ErrorGuardar'),
              ok: false,
            };
          }),
      });
  }

  /** Enviar uno de prueba es la única forma de saber que el canal funciona de verdad. */
  probarAviso(): void {
    this.probando = true;
    this.mensajeAjustes = undefined;

    this.servicioAjustes.sendTest().subscribe({
      next: r =>
        this.aplicar(() => {
          this.probando = false;
          this.mensajeAjustes = {
            texto: r.success
              ? this.textos.instant('::Alertas:Enviado')
              : (r.message ?? this.textos.instant('::Alertas:ErrorEnviar')),
            ok: r.success,
          };
        }),
      error: e =>
        this.aplicar(() => {
          this.probando = false;
          this.mensajeAjustes = {
            texto: e?.error?.error?.message ?? this.textos.instant('::Alertas:ErrorEnviar'),
            ok: false,
          };
        }),
    });
  }

  /**
   * Antigüedad del último aviso entregado.
   *
   * <p>La razón de que esto exista: un canal de avisos que dejó de funcionar se ve
   * exactamente igual que uno que no tiene nada que decir — en los dos casos no llega
   * nada.</p>
   */
  get ultimoAvisoTexto(): string {
    const iso = this.ajustes?.lastAlertUtc;
    if (!iso) {
      return this.textos.instant('::Alertas:Nunca');
    }

    const segundos = (Date.now() - new Date(iso).getTime()) / 1000;
    if (segundos < 3600) {
      return `hace ${Math.round(segundos / 60)} min`;
    }
    if (segundos < 172800) {
      return `hace ${Math.round(segundos / 3600)} h`;
    }
    return `hace ${Math.round(segundos / 86400)} días`;
  }
}
