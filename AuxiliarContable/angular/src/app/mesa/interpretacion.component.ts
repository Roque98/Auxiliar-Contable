import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { InterpretacionDeOperacionDto, TEXTO_ALERTA, TEXTO_FUENTES, TEXTO_ISR, TEXTO_IVA } from './interpretaciones.service';

/** Lo que Claude interpretó de una operación, dentro de su detalle en la mesa. */
@Component({
  selector: 'app-interpretacion',
  imports: [DatePipe],
  template: `
    <div class="ms-interpretacion" data-testid="interpretacion">
      <div class="ms-cabecera-fila">
        <h3 class="ms-subtitulo"><i class="bi bi-stars" aria-hidden="true"></i> Lo que interpretó Claude</h3>
        @if (puedeEditar()) {
          <button type="button" class="btn btn-link ms-rev-op" (click)="interpretar.emit()" [disabled]="dato()?.enLaFila">
            {{ dato()?.enLaFila ? 'En la fila…' : dato()?.interpretacion ? 'Volver a interpretar' : 'Interpretar con Claude' }}
          </button>
        }
      </div>
      @if (dato(); as d) {
        @if (d.error) { <p class="ms-aviso">{{ d.error }}</p> }
        @if (d.interpretacion; as i) {
          <p class="ms-nota">
            @if (!d.alDia) { <strong>Ya no está al día: cambió alguna fuente.</strong> · }
            Con {{ fuentes(d.fuentes) }} · confianza {{ i.confianza }}
            @if (d.interpretadaUtc) { · {{ d.interpretadaUtc | date: 'd MMM, HH:mm' }} }
          </p>
          <p><strong>{{ i.objeto }}</strong></p>
          <p class="ms-nota">
            Proveedor: {{ i.proveedor.queVende }} @if (!i.proveedor.loConozco) { (no lo reconoce) } ·
            las fuentes {{ coinciden(i.coincidenLasFuentes) }}
          </p>
          @for (x of i.diferencias; track $index) { <p class="ms-aviso">Diferencia: {{ x }}</p> }
          <div class="ms-tabla-caja">
            <table class="ms-tabla">
              <thead><tr><th scope="col">Concepto</th><th scope="col">Cuenta</th><th scope="col">ISR</th><th scope="col">IVA</th><th scope="col">Fundamento</th></tr></thead>
              <tbody>
                @for (c of i.conceptos; track $index) {
                  <tr>
                    <td>
                      {{ c.queEs }}
                      @if (c.claveCoincide === false) { <span class="ms-aviso">· la clave SAT o la unidad no corresponden</span> }
                    </td>
                    <td>{{ c.cuenta }}</td>
                    <td>{{ isr(c.isr) }} @if (c.porcentajeInversion) { ({{ c.porcentajeInversion }} % anual) }</td>
                    <td>{{ iva(c.iva) }}</td>
                    <td class="ms-nota">{{ c.fundamento }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <p><span class="ms-nota">Tratamiento fiscal:</span> {{ i.tratamientoFiscal }}</p>
          <p><span class="ms-nota">Juicio contable:</span> {{ i.juicioContable }}</p>
          @for (a of i.alertas; track $index) {
            <p class="ms-aviso"><strong>{{ alerta(a.tipo) }}:</strong> {{ a.mensaje }} @if (a.fundamento) { <span class="ms-nota">({{ a.fundamento }})</span> }</p>
          }
          @if (i.preguntasAlCliente.length) {
            <p class="ms-nota">Preguntas para el cliente:</p>
            <ul class="ms-pendientes">@for (q of i.preguntasAlCliente; track $index) { <li>{{ q }}</li> }</ul>
          }
        } @else if (d.enLaFila) {
          <p class="ms-nota"><span class="ig-girando" aria-hidden="true"></span> En la fila: Claude la interpreta en unos minutos.</p>
        }
      } @else {
        <p class="ms-nota">Claude todavía no la interpreta. Lee juntos la factura, el ticket y el estado de cuenta: qué es el objeto y qué es para el giro del cliente.</p>
      }
    </div>
  `,
  styleUrls: ['./mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InterpretacionComponent {
  readonly dato = input<InterpretacionDeOperacionDto | null | undefined>(null);
  readonly puedeEditar = input(false);
  readonly interpretar = output<void>();

  isr = (x: string) => TEXTO_ISR[x] ?? x;
  iva = (x: string) => TEXTO_IVA[x] ?? x;
  alerta = (x: string) => TEXTO_ALERTA[x] ?? x;
  fuentes = (xs: string[]) => xs.map(x => TEXTO_FUENTES[x] ?? x).join(', ') || 'lo que había';
  coinciden = (x: string) => ({ si: 'coinciden', parcial: 'coinciden en parte', no: 'NO coinciden', solo_hay_una: 'son una sola' })[x] ?? x;
}
