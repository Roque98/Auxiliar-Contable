import { CurrencyPipe, DatePipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { DocumentosService, TipoDeDocumento } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { DineroDelMesDto, DineroService, GuardarMovimientoDto, MovimientoDto, TIPOS, TipoDeMovimiento } from './dinero.service';

const MESES = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];

/** «2026-10-05» de hoy, en la zona de quien usa la pantalla (no en UTC). */
function hoyIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/**
 * Efectivo y crédito: lo que el estado de cuenta no ve. El cliente (o el despacho) anota las ventas y gastos en
 * efectivo, el corte de caja del día y lo que se compró o vendió a crédito; lo a crédito sigue apareciendo cada mes
 * hasta que se marca como pagado. Cada registro puede llevar su comprobante (se sube a la bóveda).
 */
@Component({
  selector: 'app-dinero',
  standalone: true,
  imports: [FormsModule, DatePipe, CurrencyPipe, NgTemplateOutlet],
  templateUrl: './dinero.component.html',
  styleUrls: ['./dinero.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DineroComponent implements OnInit {
  private readonly api = inject(DineroService);
  private readonly documentos = inject(DocumentosService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly meses = MESES;
  readonly tipos = TIPOS;
  readonly Tipo = TipoDeMovimiento;
  readonly anios: number[];
  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly datos = signal<DineroDelMesDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal(false);
  /** null = cerrado; 'nuevo' o el id que se está editando. */
  readonly editando = signal<string | null>(null);
  readonly delMes = computed(() => this.datos()?.movimientos.filter(m => !m.deOtroMes) ?? []);
  readonly pendientesDeAntes = computed(() => this.datos()?.movimientos.filter(m => m.deOtroMes) ?? []);

  anio: number;
  mes: number;
  forma: GuardarMovimientoDto = this.vacia();
  comprobante: File | null = null;

  constructor() {
    const hoy = new Date();
    this.anio = hoy.getFullYear();
    this.mes = hoy.getMonth() + 1;
    this.anios = [hoy.getFullYear(), hoy.getFullYear() - 1, hoy.getFullYear() - 2];
  }

  ngOnInit(): void {
    const q = this.ruta.snapshot.queryParamMap;
    this.anio = Number(q.get('anio')) || this.anio;
    this.mes = Number(q.get('mes')) || this.mes;
    this.perfiles.clientes().subscribe({
      next: lista => {
        this.clientes.set(lista);
        const elegido = lista.find(c => c.id === q.get('cliente')) ?? (lista.length === 1 ? lista[0] : null);
        this.clienteId.set(elegido?.id ?? null);
        this.cargar();
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string | null): void {
    this.clienteId.set(id);
    this.cargar();
  }

  cargar(): void {
    const id = this.clienteId();
    this.datos.set(null);
    this.editando.set(null);
    if (!id) {
      return;
    }
    void this.router.navigate([], { queryParams: { cliente: id, anio: this.anio, mes: this.mes }, replaceUrl: true });
    const periodo = `${id}|${this.anio}|${this.mes}`;
    this.api.delMes(id, this.anio, this.mes).subscribe({
      next: d => {
        if (periodo === `${this.clienteId()}|${this.anio}|${this.mes}`) {
          this.datos.set(d);
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar el efectivo y crédito del mes.')),
    });
  }

  esCredito(tipo: TipoDeMovimiento): boolean {
    return tipo === TipoDeMovimiento.CompraACredito || tipo === TipoDeMovimiento.VentaACredito;
  }

  nombreDe(tipo: TipoDeMovimiento): string {
    return this.tipos.find(t => t.tipo === tipo)?.nombre ?? '';
  }

  nuevo(tipo: TipoDeMovimiento): void {
    this.forma = { ...this.vacia(), tipo };
    this.comprobante = null;
    this.error.set(null);
    this.editando.set('nuevo');
  }

  editar(m: MovimientoDto): void {
    this.forma = {
      tipo: m.tipo,
      fecha: m.fecha.slice(0, 10),
      importe: m.importe,
      concepto: m.concepto,
      contraparte: m.contraparte,
      documentoId: m.documentoId,
      vence: m.vence?.slice(0, 10) ?? null,
      nota: m.nota,
    };
    this.comprobante = null;
    this.error.set(null);
    this.editando.set(m.id);
  }

  elegirComprobante(evento: Event): void {
    this.comprobante = (evento.target as HTMLInputElement).files?.[0] ?? null;
  }

  guardar(): void {
    const id = this.clienteId();
    const cual = this.editando();
    if (!id || !cual || this.ocupado() || !(this.forma.importe > 0) || !this.forma.concepto.trim() || !this.forma.fecha) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    const forma = { ...this.forma, vence: this.esCredito(this.forma.tipo) ? this.forma.vence || null : null };
    const enviar = () => {
      const llamada = cual === 'nuevo' ? this.api.crear(id, forma) : this.api.actualizar(id, cual, forma);
      llamada.subscribe({
        next: () => {
          this.ocupado.set(false);
          this.cargar();
        },
        error: e => {
          this.error.set(mensajeDeError(e, 'No se pudo guardar.'));
          this.ocupado.set(false);
        },
      });
    };
    if (!this.comprobante) {
      enviar();
      return;
    }
    // El comprobante va a la bóveda del cliente, en el mes del movimiento, y queda ligado.
    const [a, m] = forma.fecha.split('-').map(Number);
    this.documentos.subir(id, TipoDeDocumento.Ticket, this.comprobante, a, m).subscribe({
      next: r => {
        forma.documentoId = r.documento.id;
        enviar();
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo subir el comprobante.'));
        this.ocupado.set(false);
      },
    });
  }

  borrar(m: MovimientoDto): void {
    const id = this.clienteId();
    if (!id || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.api.borrar(id, m.id).subscribe({
      next: () => {
        this.ocupado.set(false);
        this.cargar();
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo borrar.'));
        this.ocupado.set(false);
      },
    });
  }

  /** «Ya se pagó» (o cobró) hoy; si ya estaba saldado, lo regresa a pendiente. */
  saldar(m: MovimientoDto): void {
    const id = this.clienteId();
    if (!id || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.api.saldar(id, m.id, m.saldado ? null : hoyIso()).subscribe({
      next: () => {
        this.ocupado.set(false);
        this.cargar();
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo marcar.'));
        this.ocupado.set(false);
      },
    });
  }

  private vacia(): GuardarMovimientoDto {
    const hoy = hoyIso();
    // Si se está viendo otro mes, la fecha propuesta es el primer día de ese mes.
    const enMes = this.anio && this.mes && hoy.slice(0, 7) !== `${this.anio}-${String(this.mes).padStart(2, '0')}`
      ? `${this.anio}-${String(this.mes).padStart(2, '0')}-01` : hoy;
    return { tipo: TipoDeMovimiento.EntradaEnEfectivo, fecha: enMes, importe: 0, concepto: '', contraparte: null, documentoId: null, vence: null, nota: null };
  }
}
