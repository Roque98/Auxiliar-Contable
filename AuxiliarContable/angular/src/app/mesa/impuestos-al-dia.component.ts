import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { descargarBase64, haceCuanto } from './descarga';
import { DescargaSatService, ImpuestosAlDiaDto } from './descarga-sat.service';

/** Cada cuánto se vuelve a preguntar mientras el servidor recalcula. */
const REVISAR_CADA_MS = 15_000;

/**
 * Impuestos al día: el pago provisional de ISR e IVA del mes, que el servidor recalcula solo cada vez que entra una
 * factura (de lo que sube el cliente, de lo que baja el Descargador 1 o de lo que factura el despacho). Mientras se
 * recalcula, la tarjeta vuelve a preguntar sola.
 */
@Component({
  selector: 'app-impuestos-al-dia',
  standalone: true,
  imports: [CurrencyPipe],
  templateUrl: './impuestos-al-dia.component.html',
  styleUrls: ['./mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ImpuestosAlDiaComponent {
  private readonly api = inject(DescargaSatService);

  readonly organizacionId = input.required<string>();
  readonly anio = input.required<number>();
  readonly mes = input.required<number>();

  readonly meses = MESES;
  readonly alDia = signal<ImpuestosAlDiaDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly bajando = signal(false);
  readonly verMeses = signal(false);
  haceCuanto = haceCuanto;

  private reloj: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.parar());
    effect(() => {
      const id = this.organizacionId();
      const anio = this.anio();
      const mes = this.mes();
      this.alDia.set(null);
      this.cargar(id, anio, mes);
    });
  }

  cargar(id = this.organizacionId(), anio = this.anio(), mes = this.mes()): void {
    this.parar();
    this.error.set(null);
    this.api.impuestosAlDia(id, anio, mes).subscribe({
      next: r => {
        // Llegó tarde la respuesta de otro cliente o mes: no se pinta.
        if (id !== this.organizacionId() || anio !== this.anio() || mes !== this.mes()) {
          return;
        }
        this.alDia.set(r);
        if (r.actualizando) {
          this.reloj = setTimeout(() => this.cargar(), REVISAR_CADA_MS);
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar el impuesto del mes.')),
    });
  }

  /** Lo calcula en este momento (con el papel de trabajo) y lo descarga. */
  papel(): void {
    this.bajando.set(true);
    this.error.set(null);
    this.api.calcularImpuestos(this.organizacionId(), this.anio(), this.mes()).subscribe({
      next: r => {
        this.bajando.set(false);
        if (r.resultado.archivo) {
          descargarBase64(r.resultado.archivo.nombre, r.resultado.archivo.contenidoB64,
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
        }
        this.cargar();
      },
      error: e => {
        this.bajando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo calcular el impuesto.'));
      },
    });
  }

  nombreDelMes(periodo: string): string {
    return this.meses[Number(periodo.slice(5, 7)) - 1] ?? periodo;
  }

  private parar(): void {
    if (this.reloj) {
      clearTimeout(this.reloj);
      this.reloj = null;
    }
  }
}
