import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import {
  ConciliacionDelMesDto,
  ConciliacionService,
  EstadoDeMovimiento,
  FacturaDelMotor,
  MovimientoLeido,
  NOMBRE_DE_ESTADO,
} from './conciliacion.service';

type Filtro = 'todos' | 'sin_factura' | 'con_factura';

/**
 * Conciliación bancaria de un cliente y un mes. La hace el motor fiscal del despacho
 * (motores-fiscales) con los estados de cuenta y las facturas XML que están en Documentos: la
 * pantalla solo pide y enseña. Nada se guarda; volver a conciliar recalcula con lo que haya.
 *
 * <p>Mobile first: tarjetas apiladas, montos a la derecha, acciones de 44 px.</p>
 */
@Component({
  selector: 'app-conciliacion',
  standalone: true,
  imports: [FormsModule, RouterLink, CurrencyPipe],
  templateUrl: './conciliacion.component.html',
  styleUrls: ['./conciliacion.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConciliacionComponent implements OnInit {
  private readonly api = inject(ConciliacionService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly meses = MESES;
  readonly nombreDeEstado = NOMBRE_DE_ESTADO;
  readonly anios: number[];

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly datos = signal<ConciliacionDelMesDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal(false);
  readonly filtro = signal<Filtro>('todos');

  anio: number;
  mes: number;

  readonly cliente = computed(() => this.clientes()?.find(c => c.id === this.clienteId()) ?? null);
  readonly facturaPorUuid = computed(() => {
    const mapa = new Map<string, FacturaDelMotor>();
    for (const f of this.datos()?.resultado.facturas ?? []) {
      if (f.uuid) {
        mapa.set(f.uuid, f);
      }
    }
    return mapa;
  });
  readonly cuentas = computed(() => {
    const porEstado = (e: EstadoDeMovimiento[]) =>
      (this.datos()?.resultado.movimientos ?? []).filter(m => e.includes(m.conciliacion?.estado ?? 'pendiente')).length;
    return {
      total: this.datos()?.resultado.movimientos.length ?? 0,
      conFactura: porEstado(['conciliado', 'combinacion']),
      traspasos: porEstado(['traspaso']),
      sinFactura: porEstado(['sin_factura', 'pendiente']),
    };
  });
  readonly movimientos = computed<MovimientoLeido[]>(() => {
    const todos = this.datos()?.resultado.movimientos ?? [];
    switch (this.filtro()) {
      case 'sin_factura':
        return todos.filter(m => ['sin_factura', 'pendiente'].includes(m.conciliacion?.estado ?? 'pendiente'));
      case 'con_factura':
        return todos.filter(m => ['conciliado', 'combinacion', 'traspaso'].includes(m.conciliacion?.estado ?? ''));
      default:
        return todos;
    }
  });

  constructor() {
    // El cierre se trabaja sobre el mes anterior.
    const hoy = new Date();
    const anterior = new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1);
    this.anio = anterior.getFullYear();
    this.mes = anterior.getMonth() + 1;
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
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string | null): void {
    this.clienteId.set(id);
    this.datos.set(null);
    this.error.set(null);
  }

  conciliar(): void {
    const id = this.clienteId();
    if (!id || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    this.datos.set(null);
    this.filtro.set('todos');
    void this.router.navigate([], { queryParams: { cliente: id, anio: this.anio, mes: this.mes }, replaceUrl: true });
    this.api.conciliar(id, this.anio, this.mes).subscribe({
      next: d => {
        this.datos.set(d);
        this.ocupado.set(false);
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo conciliar.'));
        this.ocupado.set(false);
      },
    });
  }

  /** Solo la terminación: el número completo no hace falta para trabajar. */
  terminacion(cuenta: string): string {
    return cuenta.length > 4 ? `…${cuenta.slice(-4)}` : cuenta;
  }

  facturas(m: MovimientoLeido): FacturaDelMotor[] {
    return (m.conciliacion?.facturas ?? []).map(u => this.facturaPorUuid().get(u)).filter((f): f is FacturaDelMotor => !!f);
  }

  estado(m: MovimientoLeido): EstadoDeMovimiento {
    return m.conciliacion?.estado ?? 'pendiente';
  }

  porcentaje(p: number | null | undefined): string {
    return p === null || p === undefined ? '' : `${Math.round(p * 100)} %`;
  }
}
