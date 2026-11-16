import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import {
  CierreDelMesDto,
  ConciliacionDelMesDto,
  ConciliacionService,
  EstadoDeMovimiento,
  EtapaDelPeriodo,
  FacturaDelMotor,
  MovimientoLeido,
  NOMBRE_DE_ESTADO,
  NOMBRE_DE_ETAPA,
  NOMBRE_DE_RESOLUCION,
  PartidaDto,
  ResolucionDePartida, CfdiDelDiagnostico, DiagnosticoDeFacturas } from './conciliacion.service';

type Filtro = 'todos' | 'sin_factura' | 'con_factura';

/**
 * El cierre del mes de un cliente, como en un sistema contable: qué falta, la conciliación bancaria
 * (la hace el motor fiscal del despacho con los estados de cuenta y las facturas XML de Documentos, y
 * se guarda), las partidas sin factura por resolver y el botón de cerrar. El despacho resuelve y
 * cierra; el cliente ve en qué va y contesta lo que se le pregunta.
 *
 * <p>Mobile first: tarjetas apiladas, montos a la derecha, acciones de 44 px.</p>
 */
@Component({
  selector: 'app-conciliacion',
  standalone: true,
  imports: [FormsModule, RouterLink, CurrencyPipe, DatePipe],
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
  readonly nombreDeEtapa = NOMBRE_DE_ETAPA;
  readonly nombreDeResolucion = NOMBRE_DE_RESOLUCION;
  readonly Etapa = EtapaDelPeriodo;
  readonly Resolucion = ResolucionDePartida;
  readonly resoluciones = [
    ResolucionDePartida.PreguntarAlCliente,
    ResolucionDePartida.TraspasoPropio,
    ResolucionDePartida.SinFactura,
    ResolucionDePartida.FacturaPorLlegar,
    ResolucionDePartida.Revisado,
  ];
  readonly anios: number[];

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly cierre = signal<CierreDelMesDto | null>(null);
  readonly datos = computed<ConciliacionDelMesDto | null>(() => this.cierre()?.conciliacion ?? null);
  readonly cargando = signal(false);
  /** Lo que el despacho va eligiendo para cada partida antes de guardarla. */
  readonly borradores = signal<Record<string, { resolucion: ResolucionDePartida; nota: string }>>({});
  readonly respuestas = signal<Record<string, string>>({});
  readonly guardando = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal(false);
  /** El diagnóstico de facturas del mes: se pide con el botón, no se guarda. */
  readonly diagnostico = signal<DiagnosticoDeFacturas | null>(null);
  readonly diagnosticando = signal(false);
  readonly errorDeDiagnostico = signal<string | null>(null);
  readonly lados = [
    { clave: 'emitidas' as const, titulo: 'Facturas que emitió', quien: 'Las corrige el cliente con quien las timbra.' },
    { clave: 'recibidas' as const, titulo: 'Facturas que recibió', quien: 'Las corrige el proveedor: hay que pedírselo.' },
  ];
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
        this.cargar();
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string | null): void {
    this.clienteId.set(id);
    this.cargar();
  }

  /** Trae cómo va el cierre del mes elegido (con su última conciliación, si la hay). */
  cargar(): void {
    const id = this.clienteId();
    this.cierre.set(null);
    this.diagnostico.set(null);
    this.errorDeDiagnostico.set(null);
    this.error.set(null);
    this.filtro.set('todos');
    if (!id) {
      return;
    }
    void this.router.navigate([], { queryParams: { cliente: id, anio: this.anio, mes: this.mes }, replaceUrl: true });
    this.cargando.set(true);
    this.api.cierre(id, this.anio, this.mes).subscribe({
      next: c => this.mostrar(c),
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo cargar el cierre del mes.'));
        this.cargando.set(false);
      },
    });
  }

  private mostrar(c: CierreDelMesDto): void {
    // Una respuesta que llega tarde (de otro cliente o mes) no pisa lo que se está viendo.
    if (c.anio !== this.anio || c.mes !== this.mes) {
      return;
    }
    const borradores: Record<string, { resolucion: ResolucionDePartida; nota: string }> = {};
    for (const p of c.partidas) {
      borradores[p.id] = { resolucion: p.resolucion, nota: p.nota ?? '' };
    }
    this.borradores.set(borradores);
    this.cierre.set(c);
    this.cargando.set(false);
  }

  borrador(p: PartidaDto): { resolucion: ResolucionDePartida; nota: string } {
    return this.borradores()[p.id] ?? { resolucion: p.resolucion, nota: p.nota ?? '' };
  }

  cambiarBorrador(p: PartidaDto, cambio: Partial<{ resolucion: ResolucionDePartida; nota: string }>): void {
    this.borradores.update(b => ({ ...b, [p.id]: { ...this.borrador(p), ...cambio } }));
  }

  /** El despacho guarda qué es una partida; vuelve a pedir el cierre para que la lista de revisión cuadre. */
  resolver(p: PartidaDto): void {
    const id = this.clienteId();
    const b = this.borrador(p);
    if (!id || this.guardando()) {
      return;
    }
    this.guardando.set(p.id);
    this.error.set(null);
    this.api.resolver(id, p.id, Number(b.resolucion), b.nota.trim() || null).subscribe({
      next: () => {
        this.guardando.set(null);
        this.cargar();
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo guardar.'));
        this.guardando.set(null);
      },
    });
  }

  escribirRespuesta(p: PartidaDto, texto: string): void {
    this.respuestas.update(r => ({ ...r, [p.id]: texto }));
  }

  /** El cliente contesta qué es un movimiento por el que le preguntaron. */
  responder(p: PartidaDto): void {
    const id = this.clienteId();
    const texto = (this.respuestas()[p.id] ?? '').trim();
    if (!id || !texto || this.guardando()) {
      return;
    }
    this.guardando.set(p.id);
    this.error.set(null);
    this.api.responder(id, p.id, texto).subscribe({
      next: () => {
        this.guardando.set(null);
        this.cargar();
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo enviar tu respuesta.'));
        this.guardando.set(null);
      },
    });
  }

  cerrar(): void {
    this.cambiarPeriodo(id => this.api.cerrar(id, this.anio, this.mes), 'No se pudo cerrar el mes.');
  }

  reabrir(): void {
    this.cambiarPeriodo(id => this.api.reabrir(id, this.anio, this.mes), 'No se pudo reabrir el mes.');
  }

  private cambiarPeriodo(llamada: (id: string) => ReturnType<ConciliacionService['cerrar']>, falla: string): void {
    const id = this.clienteId();
    if (!id || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    llamada(id).subscribe({
      next: c => {
        this.ocupado.set(false);
        this.mostrar(c);
      },
      error: e => {
        this.error.set(mensajeDeError(e, falla));
        this.ocupado.set(false);
      },
    });
  }

  /** Facturas del mes contra sus complementos de pago y notas de crédito: qué no cuadra y por qué. */
  diagnosticar(): void {
    const id = this.clienteId();
    if (!id || this.diagnosticando()) {
      return;
    }
    const periodo = `${id}|${this.anio}|${this.mes}`;
    this.diagnosticando.set(true);
    this.errorDeDiagnostico.set(null);
    this.api.diagnosticarFacturas(id, this.anio, this.mes).subscribe({
      next: d => {
        // Una respuesta que llega tarde (de otro cliente o mes) no pisa lo que se está viendo.
        if (periodo === `${this.clienteId()}|${this.anio}|${this.mes}`) {
          this.diagnostico.set(d);
        }
        this.diagnosticando.set(false);
      },
      error: e => {
        this.errorDeDiagnostico.set(mensajeDeError(e, 'No se pudieron revisar las facturas.'));
        this.diagnosticando.set(false);
      },
    });
  }

  /** «A-15 · 2026-10-03 · PPD · $5,000.00»: lo que identifica un CFDI en pantalla. */
  etiqueta(c: CfdiDelDiagnostico): string {
    if (c.falta) {
      return `${c.uuid.slice(0, 8)} (no está en la bóveda)`;
    }
    const total = c.total == null ? '' : ` · ${c.total.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' })}`;
    return [c.serieFolio || c.uuid.slice(0, 8), c.fecha, c.metodo].filter(Boolean).join(' · ') + total;
  }

  conciliar(): void {
    const id = this.clienteId();
    if (!id || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    this.filtro.set('todos');
    this.api.conciliar(id, this.anio, this.mes).subscribe({
      next: () => {
        this.ocupado.set(false);
        this.cargar();
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
