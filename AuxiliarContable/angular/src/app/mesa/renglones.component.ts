import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Un renglón de un papel de trabajo o de un estado financiero, como lo manda el motor.
 * tipo: renglon, subtotal, resultado, titulo, tasa (en %), factor o nota (solo explica).
 */
export interface RenglonDto {
  concepto: string;
  importe: number | null;
  fundamento: string;
  tipo: string;
}

/** Tabla «concepto · importe · fundamento» del papel de impuestos y de los estados financieros. */
@Component({
  selector: 'app-renglones',
  imports: [CurrencyPipe, DecimalPipe],
  template: `
    <div class="ms-tabla-caja">
      <table class="ms-tabla ms-papel">
        @if (titulo()) { <caption>{{ titulo() }}</caption> }
        <thead><tr><th scope="col">Concepto</th><th scope="col" class="ms-num">Importe</th><th scope="col">Fundamento</th></tr></thead>
        <tbody>
          @for (r of renglones(); track $index) {
            <tr [class.ms-total]="r.tipo === 'subtotal' || r.tipo === 'resultado' || r.tipo === 'total'"
                [class.ms-papel-resultado]="r.tipo === 'resultado'" [class.ms-papel-titulo]="r.tipo === 'titulo'">
              @if (r.tipo === 'titulo') {
                <th scope="rowgroup" colspan="3">{{ r.concepto }}</th>
              } @else {
                <td>{{ r.concepto }}</td>
                <td class="ms-num">
                  @if (r.tipo === 'nota') {
                    <span class="ms-nota">ver fundamento</span>
                  } @else if (r.importe === null) {
                    —
                  } @else if (r.tipo === 'tasa') {
                    {{ r.importe | number: '1.1-2' }} %
                  } @else if (r.tipo === 'factor') {
                    {{ r.importe | number: '1.4-4' }}
                  } @else {
                    {{ r.importe | currency: 'MXN' : 'symbol-narrow' }}
                  }
                </td>
                <td class="ms-nota">{{ r.fundamento }}</td>
              }
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styleUrls: ['./mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RenglonesComponent {
  readonly renglones = input.required<RenglonDto[]>();
  readonly titulo = input<string>('');
}
