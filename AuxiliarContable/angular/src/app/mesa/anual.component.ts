import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { AnualDto, CapturaDeAnualDto, DescargaSatService } from './descarga-sat.service';
import { RenglonesComponent } from './renglones.component';
import { aNumero } from './anual';

/** Un dato que no viene en los CFDI, con para quién aplica. */
interface CampoDeAnual {
  clave: keyof CapturaDeAnualDto;
  etiqueta: string;
  ayuda: string;
}

/**
 * Declaración anual de ISR en la mesa: la calcula el motor en el momento con las facturas del ejercicio y lo que se
 * captura aquí (lo que no viene en los CFDI). Renglón por renglón con su fundamento; no se guarda.
 */
@Component({
  selector: 'app-anual',
  imports: [CurrencyPipe, DecimalPipe, FormsModule, RenglonesComponent],
  templateUrl: './anual.component.html',
  styleUrls: ['./mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnualComponent {
  private readonly api = inject(DescargaSatService);

  readonly organizacionId = input.required<string>();
  readonly anio = input.required<number>();

  readonly anual = signal<AnualDto | null>(null);
  readonly calculando = signal(false);
  readonly error = signal<string | null>(null);
  readonly verDatos = signal(false);

  readonly campos: CampoDeAnual[] = [
    { clave: 'ptuPagada', etiqueta: 'PTU pagada en el ejercicio', ayuda: 'Personas morales y actividad empresarial (arts. 9 y 109).' },
    { clave: 'perdidasAnteriores', etiqueta: 'Pérdidas fiscales pendientes, actualizadas', ayuda: 'De ejercicios anteriores (arts. 57 y 109).' },
    { clave: 'umaAnual', etiqueta: 'UMA anual', ayuda: 'Para el tope de deducciones personales: 5 UMA anuales (art. 151).' },
    { clave: 'inventarioInicial', etiqueta: 'Inventario inicial', ayuda: 'Personas morales que venden mercancía: costo de lo vendido (art. 39).' },
    { clave: 'inventarioFinal', etiqueta: 'Inventario final', ayuda: 'Al 31 de diciembre.' },
    { clave: 'sueldosGravados', etiqueta: 'Sueldos gravados de otro patrón', ayuda: 'Personas físicas: de la constancia de sueldos (art. 152).' },
    { clave: 'isrRetenidoSueldos', etiqueta: 'ISR retenido por sueldos', ayuda: 'De la misma constancia.' },
    { clave: 'pagosProvisionales', etiqueta: 'Pagos provisionales realmente pagados', ayuda: 'Vacío: los que salen del cálculo de cada mes.' },
  ];
  texto: Record<string, string> = {};

  constructor() {
    effect(() => {
      this.organizacionId();
      this.anio();
      this.anual.set(null);
      this.error.set(null);
    });
  }

  captura(): CapturaDeAnualDto {
    const c = {} as CapturaDeAnualDto;
    for (const x of this.campos) {
      c[x.clave] = aNumero(this.texto[x.clave]);
    }
    return c;
  }

  calcular(): void {
    const [id, anio] = [this.organizacionId(), this.anio()];
    this.calculando.set(true);
    this.error.set(null);
    this.api.calcularAnual(id, anio, this.captura()).subscribe({
      next: a => {
        this.calculando.set(false);
        if (id === this.organizacionId() && anio === this.anio()) {
          this.anual.set(a);
        }
      },
      error: e => {
        this.calculando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo calcular la anual.'));
      },
    });
  }
}
