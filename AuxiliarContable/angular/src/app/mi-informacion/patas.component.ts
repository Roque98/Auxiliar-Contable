import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { DocumentosService } from '../documentos/documentos.service';
import { TipoDeDocumento } from '../documentos/tipos';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import {
  EstadoDePata,
  FormaDeTransaccion,
  GuardarPataDto,
  MOTIVOS,
  NOMBRES_DE_CATEGORIA,
  OperacionDto,
  OperacionesService,
  SentidoDeOperacion,
  datosDe,
} from './operaciones.service';
import { Accion, FacturaDeLaOperacion, Opcion, Pregunta, siguientePregunta } from './guia';

type Pata = 'factura' | 'transaccion' | 'controlInterno';

/**
 * Las tres patas de una operación (factura · transacción · control interno) con su círculo y lo que se puede
 * contestar en cada una: amarrarla (UUID, referencia o folio, efectivo, crédito, el comprobante), decir que no hay,
 * o dejarla para después. Se usa al subir algo, al pedir una factura y en «Todo».
 */
@Component({
  selector: 'app-patas',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, DatePipe],
  templateUrl: './patas.component.html',
  styleUrls: ['./patas.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatasComponent {
  private readonly api = inject(OperacionesService);
  private readonly documentos = inject(DocumentosService);

  readonly organizacionId = input.required<string>();
  readonly operacion = input.required<OperacionDto>();
  /** Otras operaciones del mes, para juntarla con una. */
  readonly otras = input<OperacionDto[]>([]);
  readonly puedeEditar = input(true);
  /** La guía del trabajo diario (preguntas para completar las tres patas). En la conciliación mensual no se usa. */
  readonly guiar = input(true);
  /** La operación como quedó (puede ser otra si se juntó), o null si se borró. */
  readonly cambio = output<OperacionDto | null>();

  readonly Estado = EstadoDePata;
  readonly Forma = FormaDeTransaccion;
  readonly Sentido = SentidoDeOperacion;
  readonly motivos = MOTIVOS;
  readonly categorias = NOMBRES_DE_CATEGORIA;

  readonly o = signal<OperacionDto | null>(null);
  readonly ocupado = signal(false);
  readonly error = signal<string | null>(null);
  readonly borrando = signal(false);

  // La guía: lo que dice el XML de la factura, una pregunta de seguimiento elegida y una advertencia por confirmar.
  readonly factura = signal<FacturaDeLaOperacion | null>(null);
  private facturaDe: string | null = null;
  readonly preguntaExtra = signal<Pregunta | null>(null);
  readonly porConfirmar = signal<Opcion | null>(null);
  private subidaPendiente: Extract<Accion, { tipo: 'subir' }> | null = null;
  readonly aceptaSubida = signal('');

  readonly pregunta = computed<Pregunta | null>(() => {
    const o = this.o();
    if (!o || !this.guiar() || !this.puedeEditar()) {
      return null;
    }
    return this.preguntaExtra() ?? siguientePregunta(o, this.factura());
  });

  // Lo que se va escribiendo.
  descripcion = '';
  importe: number | null = null;
  sentido = SentidoDeOperacion.Salida;
  uuid = '';
  forma = FormaDeTransaccion.Banco;
  referencia = '';
  vence = '';
  juntarCon: string | null = null;

  /** Las que le completan algo: tienen verde justo donde esta no tiene nada. */
  readonly candidatas = computed(() => {
    const o = this.o();
    if (!o) {
      return [];
    }
    const patas: Pata[] = ['factura', 'transaccion', 'controlInterno'];
    return this.otras()
      .filter(x => x.id !== o.id && patas.some(p => o[p].estado === EstadoDePata.Pendiente && x[p].estado === EstadoDePata.Ligada))
      // Primero las del mismo importe.
      .sort((a, b) => Number(b.importe === o.importe) - Number(a.importe === o.importe));
  });

  constructor() {
    effect(() => this.poner(this.operacion()));
  }

  private poner(o: OperacionDto): void {
    this.o.set(o);
    this.preguntaExtra.set(null);
    this.porConfirmar.set(null);
    // El XML de la factura se lee una vez por documento (tipo, método y forma de pago).
    const doc = o.factura.estado === EstadoDePata.Ligada ? o.factura.documentoId : null;
    if (doc !== this.facturaDe) {
      this.facturaDe = doc;
      this.factura.set(null);
      if (doc && this.guiar()) {
        this.api.factura(this.organizacionId(), o.id).subscribe({ next: f => this.facturaDe === doc && this.factura.set(f), error: () => undefined });
      }
    }
    this.descripcion = o.descripcion;
    this.importe = o.importe;
    this.sentido = o.sentido;
    this.uuid = o.factura.referencia ?? '';
    this.forma = o.transaccion.forma ?? FormaDeTransaccion.Banco;
    this.referencia = o.transaccion.referencia ?? '';
    this.vence = o.transaccion.vence?.substring(0, 10) ?? '';
    this.juntarCon = null;
  }

  clase(e: EstadoDePata): string {
    return e === EstadoDePata.Ligada ? 'pt-circulo pt-ligada' : e === EstadoDePata.NoHay ? 'pt-circulo pt-nohay' : 'pt-circulo pt-pendiente';
  }

  etiqueta(e: EstadoDePata): string {
    return e === EstadoDePata.Ligada ? 'amarrada' : e === EstadoDePata.NoHay ? 'no hay' : 'pendiente';
  }

  guardarDatos(): void {
    this.guardar({});
  }

  ligarFactura(): void {
    this.guardar({ factura: { estado: EstadoDePata.Ligada, referencia: this.uuid.trim() || null, documentoId: this.o()?.factura.documentoId } });
  }

  ligarTransaccion(): void {
    this.guardar({
      transaccion: {
        estado: EstadoDePata.Ligada,
        forma: this.forma,
        referencia: this.referencia.trim() || null,
        documentoId: this.forma === FormaDeTransaccion.Banco ? this.o()?.transaccion.documentoId : null,
        vence: this.forma === FormaDeTransaccion.Credito && this.vence ? this.vence : null,
      },
    });
  }

  noHay(pata: Pata, motivo: string): void {
    this.guardar({ [pata]: { estado: EstadoDePata.NoHay, motivo } });
  }

  despues(pata: Pata): void {
    this.guardar({ [pata]: { estado: EstadoDePata.Pendiente } });
  }

  /** Sube el ticket, la nota o el comprobante (al mes de la operación) y lo amarra a su pata. */
  subirA(evento: Event, pata: 'transaccion' | 'controlInterno'): void {
    const entrada = evento.target as HTMLInputElement;
    const archivo = entrada.files?.[0];
    entrada.value = '';
    const o = this.o();
    if (!archivo || !o) {
      return;
    }
    const fecha = new Date(o.fecha);
    const tipo = pata === 'transaccion' ? TipoDeDocumento.ComprobanteDePago : TipoDeDocumento.ControlInterno;
    this.ocupado.set(true);
    this.error.set(null);
    this.documentos.subir(this.organizacionId(), tipo, archivo, fecha.getUTCFullYear(), fecha.getUTCMonth() + 1).subscribe({
      next: r => {
        this.ocupado.set(false);
        const pataNueva: GuardarPataDto = pata === 'transaccion'
          ? { estado: EstadoDePata.Ligada, forma: FormaDeTransaccion.Banco, referencia: this.referencia.trim() || null, documentoId: r.documento.id }
          : { estado: EstadoDePata.Ligada, documentoId: r.documento.id };
        this.guardar({ [pata]: pataNueva });
      },
      error: e => {
        this.ocupado.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo subir.'));
      },
    });
  }

  juntar(): void {
    const o = this.o();
    if (!o || !this.juntarCon) {
      return;
    }
    this.llamar(this.api.juntar(this.organizacionId(), o.id, this.juntarCon));
  }

  borrar(): void {
    const o = this.o();
    if (!o) {
      return;
    }
    if (!this.borrando()) {
      // Primer toque: pide confirmar (sin diálogos del navegador).
      this.borrando.set(true);
      return;
    }
    this.ocupado.set(true);
    this.api.borrar(this.organizacionId(), o.id).subscribe({
      next: () => {
        this.ocupado.set(false);
        this.cambio.emit(null);
      },
      error: e => {
        this.ocupado.set(false);
        this.borrando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo borrar.'));
      },
    });
  }

  /** Una opción de la guía: si trae advertencia, primero se enseña y se confirma. */
  elegirOpcion(op: Opcion, entrada: HTMLInputElement): void {
    if (op.advertencia && this.porConfirmar() !== op) {
      this.porConfirmar.set(op);
      return;
    }
    this.porConfirmar.set(null);
    const a = op.accion;
    switch (a.tipo) {
      case 'preguntar':
        this.preguntaExtra.set(a.pregunta);
        return;
      case 'guardar':
        this.preguntaExtra.set(null);
        this.guardar({ [a.pata]: { estado: a.respuesta.estado, motivo: a.respuesta.motivo ?? null, forma: a.respuesta.forma ?? null } });
        return;
      case 'subir':
        this.subidaPendiente = a;
        this.aceptaSubida.set(a.acepta);
        // El input se abre con el clic del usuario (mismo gesto).
        entrada.accept = a.acepta;
        entrada.click();
        return;
    }
  }

  cancelarConfirmacion(): void {
    this.porConfirmar.set(null);
  }

  volverAPreguntar(): void {
    this.preguntaExtra.set(null);
    this.porConfirmar.set(null);
  }

  /** Lo que se subió desde la guía: va a la bóveda (al mes de la operación) y se amarra a su pata. */
  subirDeLaGuia(evento: Event): void {
    const entrada = evento.target as HTMLInputElement;
    const archivo = entrada.files?.[0];
    entrada.value = '';
    const a = this.subidaPendiente;
    const o = this.o();
    this.subidaPendiente = null;
    if (!archivo || !a || !o) {
      return;
    }
    const fecha = new Date(o.fecha);
    this.ocupado.set(true);
    this.error.set(null);
    this.documentos.subir(this.organizacionId(), a.documento, archivo, fecha.getUTCFullYear(), fecha.getUTCMonth() + 1).subscribe({
      next: r => {
        this.ocupado.set(false);
        const id = r.documento.id;
        let pata: GuardarPataDto;
        if (a.pata === 'factura') {
          pata = { estado: EstadoDePata.Ligada, documentoId: id };
        } else if (a.pata === 'transaccion' && a.documento === TipoDeDocumento.Cfdi) {
          // El complemento de pago queda en la bóveda; el pago se amarra con el estado de cuenta.
          pata = { estado: EstadoDePata.Pendiente, motivo: 'Se amarra con el estado de cuenta al conciliar (complemento de pago subido).' };
        } else if (a.pata === 'transaccion') {
          pata = { estado: EstadoDePata.Ligada, forma: FormaDeTransaccion.Banco, documentoId: id };
        } else {
          pata = { estado: EstadoDePata.Ligada, documentoId: id };
        }
        this.preguntaExtra.set(null);
        this.guardar({ [a.pata]: pata });
      },
      error: e => {
        this.ocupado.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo subir.'));
      },
    });
  }

  private guardar(patas: Partial<Record<Pata, GuardarPataDto>>): void {
    const o = this.o();
    if (!o) {
      return;
    }
    const datos = {
      ...datosDe(o),
      descripcion: this.descripcion.trim() || o.descripcion,
      importe: this.importe && this.importe > 0 ? this.importe : null,
      sentido: this.sentido,
      ...patas,
    };
    this.llamar(this.api.actualizar(this.organizacionId(), o.id, datos));
  }

  private llamar(llamada: Observable<OperacionDto>): void {
    this.ocupado.set(true);
    this.error.set(null);
    llamada.subscribe({
      next: r => {
        this.ocupado.set(false);
        this.poner(r);
        this.cambio.emit(r);
      },
      error: e => {
        this.ocupado.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo guardar.'));
      },
    });
  }
}
