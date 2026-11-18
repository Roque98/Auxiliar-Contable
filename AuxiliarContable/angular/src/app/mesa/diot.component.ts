import { CurrencyPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { descargarBase64 } from './descarga';
import { DescargaSatService, DiotDto, RenglonDeDiotDto } from './descarga-sat.service';

/**
 * DIOT del mes en la mesa: la arma el motor en el momento con las facturas recibidas (la del Armador: por flujo de
 * pagos, LIVA 5-III), enseña un renglón por proveedor con de dónde sale cada peso, lo que no entra y por qué, y baja el
 * archivo de carga masiva del SAT.
 */
@Component({
  selector: 'app-diot',
  imports: [CurrencyPipe, PercentPipe],
  templateUrl: './diot.component.html',
  styleUrls: ['./mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DiotComponent {
  private readonly api = inject(DescargaSatService);

  readonly organizacionId = input.required<string>();
  readonly anio = input.required<number>();
  readonly mes = input.required<number>();

  readonly meses = MESES;
  readonly diot = signal<DiotDto | null>(null);
  readonly armando = signal(false);
  readonly error = signal<string | null>(null);
  /** RFC y tipo de operación del proveedor abierto en su detalle. */
  readonly abierto = signal<string | null>(null);

  constructor() {
    // Otro cliente o mes: lo armado ya no es de aquí.
    effect(() => {
      this.organizacionId();
      this.anio();
      this.mes();
      this.diot.set(null);
      this.error.set(null);
      this.abierto.set(null);
    });
  }

  armar(): void {
    const [id, anio, mes] = [this.organizacionId(), this.anio(), this.mes()];
    this.armando.set(true);
    this.error.set(null);
    this.api.diot(id, anio, mes).subscribe({
      next: d => {
        this.armando.set(false);
        if (id === this.organizacionId() && anio === this.anio() && mes === this.mes()) {
          this.diot.set(d);
        }
      },
      error: e => {
        this.armando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo armar la DIOT.'));
      },
    });
  }

  descargar(d: DiotDto): void {
    if (d.resultado.archivo) {
      descargarBase64(d.resultado.archivo.nombre, d.resultado.archivo.contenidoB64, 'text/plain;charset=utf-8');
    }
  }

  clave(r: RenglonDeDiotDto): string {
    return `${r.rfc}|${r.tipoOperacion}`;
  }

  alternar(r: RenglonDeDiotDto): void {
    this.abierto.set(this.abierto() === this.clave(r) ? null : this.clave(r));
  }

  /** Valor de los actos al 8 % (frontera norte y sur). */
  ocho(r: { valorFn: number; valorFs: number }): number {
    return r.valorFn + r.valorFs;
  }

  /** IVA que no se acredita: por proporción (LIVA 5-V) o porque no cumple requisitos. */
  noAcreditable(r: { noProp16: number; noReq16: number }): number {
    return r.noProp16 + r.noReq16;
  }
}
