import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { MAX_MESES_POR_ORDEN, descargarBlob, mesesDeLaOrden, textoDelPaquete } from './descarga';
import { DescargaDelSatDto, DescargaSatService, ResultadoDelPaqueteDto } from './descarga-sat.service';

/**
 * Descarga del SAT e impuestos, dentro de la mesa de trabajo. Si el cliente no entregó nada, se marca el mes y se
 * trabaja solo con lo del SAT: (1) se baja la orden para el Descargador 1, que en la computadora del despacho entra al
 * portal (alguien inicia sesión con el captcha) y baja lo que falta, lo dañado y el estatus de todo; (2) se sube el
 * paquete que deja; y el impuesto del mes se recalcula solo (ver «Impuestos al día»).
 */
@Component({
  selector: 'app-descarga-sat',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './descarga-sat.component.html',
  styleUrls: ['./mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DescargaSatComponent {
  private readonly api = inject(DescargaSatService);

  readonly organizacionId = input.required<string>();
  readonly anio = input.required<number>();
  readonly mes = input.required<number>();
  readonly puedeEditar = input(true);

  readonly meses = MESES;
  readonly anios = Array.from({ length: 6 }, (_, i) => new Date().getFullYear() - i);
  readonly maxMeses = MAX_MESES_POR_ORDEN;

  readonly estado = signal<DescargaDelSatDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal<string | null>(null);
  readonly paquete = signal<ResultadoDelPaqueteDto | null>(null);

  desdeAnio = new Date().getFullYear();
  desdeMes = 1;
  hastaAnio = new Date().getFullYear();
  hastaMes = 1;
  emitidas = true;
  recibidas = true;

  constructor() {
    effect(() => {
      const id = this.organizacionId();
      const anio = this.anio();
      const mes = this.mes();
      this.desdeAnio = this.hastaAnio = anio;
      this.desdeMes = this.hastaMes = mes;
      this.paquete.set(null);
      this.cargar(id, anio, mes);
    });
  }

  get mesesPedidos(): number {
    return mesesDeLaOrden(this.desdeAnio, this.desdeMes, this.hastaAnio, this.hastaMes);
  }

  textoDelPaquete = textoDelPaquete;

  cargar(id = this.organizacionId(), anio = this.anio(), mes = this.mes()): void {
    this.error.set(null);
    this.api.estado(id, anio, mes).subscribe({
      next: e => this.estado.set(e),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar lo descargado del SAT.')),
    });
  }

  marcar(sinEntrega: boolean): void {
    this.api.marcarSinEntrega(this.organizacionId(), this.anio(), this.mes(), sinEntrega).subscribe({
      next: () => {
        // Sin nada del cliente, la orden más útil es el ejercicio hasta el mes.
        if (sinEntrega) {
          this.desdeMes = 1;
          this.desdeAnio = this.anio();
        }
        this.cargar();
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo guardar.')),
    });
  }

  bajarOrden(): void {
    this.ocupado.set('orden');
    this.error.set(null);
    this.api.orden(this.organizacionId(), this.desdeAnio, this.desdeMes, this.hastaAnio, this.hastaMes, this.emitidas, this.recibidas).subscribe({
      next: o => {
        this.ocupado.set(null);
        descargarBlob(o.nombreDeArchivo, new Blob([o.contenido], { type: 'application/json' }));
      },
      error: e => {
        this.ocupado.set(null);
        this.error.set(mensajeDeError(e, 'No se pudo armar la orden.'));
      },
    });
  }

  subir(evento: Event): void {
    const campo = evento.target as HTMLInputElement;
    const archivo = campo.files?.[0];
    campo.value = '';
    if (!archivo) {
      return;
    }
    this.ocupado.set('paquete');
    this.error.set(null);
    this.paquete.set(null);
    this.api.subirPaquete(this.organizacionId(), archivo).subscribe({
      next: r => {
        this.ocupado.set(null);
        this.paquete.set(r);
        this.cargar();
      },
      error: e => {
        this.ocupado.set(null);
        this.error.set(mensajeDeError(e, 'No se pudo subir el paquete.'));
      },
    });
  }
}
