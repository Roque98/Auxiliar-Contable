import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { MAX_DIAS_POR_ORDEN, MAX_FOLIOS_POR_ORDEN, MAX_MESES_POR_ORDEN, descargarBlob, diasDeLaOrden, foliosEn, mesesDeLaOrden, textoDelPaquete } from './descarga';
import { OrdenDeDescargaDto } from './descarga-sat.service';
import { Observable } from 'rxjs';
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
  readonly modos = [
    { valor: 'meses' as const, etiqueta: 'Por meses' },
    { valor: 'anio' as const, etiqueta: 'Año completo' },
    { valor: 'dias' as const, etiqueta: 'Por días' },
    { valor: 'folios' as const, etiqueta: 'Por folio' },
  ];
  modo: 'meses' | 'anio' | 'dias' | 'folios' = 'meses';
  anioCompleto = new Date().getFullYear();
  desdeDia = '';
  hastaDia = '';
  textoFolios = '';

  constructor() {
    effect(() => {
      const id = this.organizacionId();
      const anio = this.anio();
      const mes = this.mes();
      this.desdeAnio = this.hastaAnio = this.anioCompleto = anio;
      this.desdeMes = this.hastaMes = mes;
      const dd = (d: number) => `${anio}-${String(mes).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
      this.desdeDia = dd(1);
      this.hastaDia = dd(new Date(anio, mes, 0).getDate());
      this.paquete.set(null);
      this.cargar(id, anio, mes);
    });
  }

  get foliosPegados(): string[] {
    return foliosEn(this.textoFolios);
  }

  /** Por qué no se puede bajar la orden todavía (null = se puede). */
  get problemaDeLaOrden(): string | null {
    if (!this.emitidas && !this.recibidas) {
      return 'Elige emitidas, recibidas o las dos.';
    }
    switch (this.modo) {
      case 'meses': {
        const n = mesesDeLaOrden(this.desdeAnio, this.desdeMes, this.hastaAnio, this.hastaMes);
        return n < 1 ? 'El mes final es antes del inicial.' : n > MAX_MESES_POR_ORDEN ? `Una orden abarca hasta ${MAX_MESES_POR_ORDEN} meses: pártela en dos.` : null;
      }
      case 'dias': {
        const n = diasDeLaOrden(this.desdeDia, this.hastaDia);
        return n === null ? 'Elige los dos días.' : n < 1 ? 'El día final es antes del inicial.'
          : n > MAX_DIAS_POR_ORDEN ? `Por días abarca hasta ${MAX_DIAS_POR_ORDEN}: para más, pídela por meses.` : null;
      }
      case 'folios': {
        const n = this.foliosPegados.length;
        return n === 0 ? 'Pega al menos un folio fiscal (UUID).' : n > MAX_FOLIOS_POR_ORDEN ? `Hasta ${MAX_FOLIOS_POR_ORDEN} folios por orden.` : null;
      }
      default:
        return null;
    }
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
    const id = this.organizacionId();
    let pedido: Observable<OrdenDeDescargaDto>;
    switch (this.modo) {
      case 'anio': {
        // El año en curso, hasta el mes de hoy: el resto todavía no existe.
        const hoy = new Date();
        const hasta = this.anioCompleto === hoy.getFullYear() ? hoy.getMonth() + 1 : 12;
        pedido = this.api.orden(id, this.anioCompleto, 1, this.anioCompleto, hasta, this.emitidas, this.recibidas);
        break;
      }
      case 'dias':
        pedido = this.api.ordenPorDias(id, this.desdeDia, this.hastaDia, this.emitidas, this.recibidas);
        break;
      case 'folios':
        pedido = this.api.ordenPorFolios(id, this.foliosPegados, this.emitidas, this.recibidas);
        break;
      default:
        pedido = this.api.orden(id, this.desdeAnio, this.desdeMes, this.hastaAnio, this.hastaMes, this.emitidas, this.recibidas);
    }
    pedido.subscribe({
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
