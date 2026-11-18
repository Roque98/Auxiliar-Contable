import { CurrencyPipe, DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import {
  EstadoDePata,
  NOMBRES_DE_CATEGORIA,
  OperacionDto,
  OperacionesService,
  SentidoDeOperacion,
} from '../mi-informacion/operaciones.service';
import { PatasComponent } from '../mi-informacion/patas.component';
import { leerSaldos } from './saldos';
import { haceCuanto } from './descarga';
import { DescargaSatComponent } from './descarga-sat.component';
import { ImpuestosAlDiaComponent } from './impuestos-al-dia.component';
import { DiotComponent } from './diot.component';
import { AnualComponent } from './anual.component';
import { RenglonesComponent } from './renglones.component';
import { mesLargo, renglonesDeResultado, renglonesDeSituacion } from './estados';
import { Auxiliar, RenglonDeBalanzaDto, auxiliarDe } from './auxiliar';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import {
  ContabilidadAlDiaDto,
  ContabilidadElectronicaDto,
  MesaDelMesDto,
  SaldoDeAperturaDto,
  SaldosDeAperturaDto,
  MesaService,
  PolizasDelMesDto,
  RenglonDeRevisionDto,
  ResultadoDeAmarreDto,
  SugerenciaDeAmarreDto,
} from './mesa.service';

type Filtro = 'todas' | 'sin-factura' | 'sin-transaccion' | 'sin-control' | 'completas';

/**
 * La mesa de trabajo del despacho: el mes de un cliente con todas sus operaciones, lo que Claude leyó de cada
 * documento y las dos tareas del auxiliar: analizar lo que subió el cliente y amarrar el banco conciliado.
 */
@Component({
  selector: 'app-mesa',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, DatePipe, DecimalPipe, PercentPipe, RouterLink, PatasComponent, DescargaSatComponent, ImpuestosAlDiaComponent, DiotComponent, AnualComponent, RenglonesComponent],
  templateUrl: './mesa.component.html',
  styleUrls: ['./mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MesaComponent implements OnInit {
  private readonly api = inject(MesaService);
  private readonly operacionesApi = inject(OperacionesService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly meses = MESES;
  readonly Estado = EstadoDePata;
  readonly Sentido = SentidoDeOperacion;
  readonly categorias = NOMBRES_DE_CATEGORIA;
  readonly anios = Array.from({ length: 4 }, (_, i) => new Date().getFullYear() - i);

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  anio = new Date().getFullYear();
  mes = new Date().getMonth() + 1;

  readonly mesa = signal<MesaDelMesDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly analizando = signal<string | null>(null);
  readonly avisosDeAnalisis = signal<string[]>([]);
  readonly amarrando = signal(false);
  readonly amarre = signal<ResultadoDeAmarreDto | null>(null);
  readonly filtro = signal<Filtro>('todas');
  readonly abierta = signal<string | null>(null);

  readonly operaciones = computed(() => this.mesa()?.operaciones.operaciones ?? []);

  // Pólizas del mes
  readonly polizas = signal<PolizasDelMesDto | null>(null);
  readonly generando = signal(false);
  readonly polizaAbierta = signal<string | null>(null);
  readonly verBalanza = signal(false);
  readonly verCierre = signal(false);
  /** La contabilidad al día: se concilia, se amarra lo seguro y se rehacen las pólizas solas. */
  readonly alDia = signal<ContabilidadAlDiaDto | null>(null);
  haceCuanto = haceCuanto;
  private relojAlDia: ReturnType<typeof setTimeout> | null = null;

  // Saldos de apertura y contabilidad electrónica
  readonly apertura = signal<SaldosDeAperturaDto | null>(null);
  readonly editandoApertura = signal(false);
  textoApertura = '';
  anioApertura = new Date().getFullYear();
  mesApertura = 1;
  readonly electronica = signal<ContabilidadElectronicaDto | null>(null);
  readonly renglonesDeResultado = renglonesDeResultado;
  readonly renglonesDeSituacion = renglonesDeSituacion;
  readonly mesLargo = mesLargo;
  /** La cuenta de la balanza abierta en su auxiliar. */
  readonly cuentaAbierta = signal<string | null>(null);
  readonly auxiliar = computed<Auxiliar | null>(() => {
    const e = this.electronica();
    const c = this.cuentaAbierta();
    const fila = c ? e?.resultado.balanza.find(b => b.cuenta === c) : undefined;
    return fila && e ? auxiliarDe(fila, e.resultado.movimientos ?? []) : null;
  });

  abrirAuxiliar(b: RenglonDeBalanzaDto): void {
    this.cuentaAbierta.set(this.cuentaAbierta() === b.cuenta ? null : b.cuenta);
  }
  readonly generandoXml = signal(false);
  /** Lo que se pegó en el editor de apertura, ya leído. */
  get aperturaLeida(): { saldos: SaldoDeAperturaDto[]; deudores: number; acreedores: number; malos: number } {
    return leerSaldos(this.textoApertura);
  }
  /** Lo que se captura para el costo de ventas del mes. */
  inventarioInicial: number | null = null;
  inventarioFinal: number | null = null;
  costoVendido: number | null = null;
  /** La revisión por operación, lo que detiene primero. */
  readonly revision = computed(() => {
    const r = this.polizas()?.resultado.revision ?? [];
    const orden = { detiene: 0, aviso: 1, aceptado: 2 } as const;
    return [...r].sort((a, b) => orden[a.nivel] - orden[b.nivel]);
  });
  readonly totales = computed(() => {
    const b = this.polizas()?.resultado.balanza ?? [];
    return { debe: b.reduce((s, x) => s + x.debe, 0), haber: b.reduce((s, x) => s + x.haber, 0) };
  });

  readonly cuentas = computed(() => {
    const l = this.operaciones();
    return {
      todas: l.length,
      sinFactura: l.filter(o => o.factura.estado === EstadoDePata.Pendiente).length,
      sinTransaccion: l.filter(o => o.transaccion.estado === EstadoDePata.Pendiente).length,
      sinControl: l.filter(o => o.controlInterno.estado === EstadoDePata.Pendiente).length,
      completas: l.filter(o => o.respondidas === 3).length,
    };
  });

  readonly visibles = computed(() => {
    const l = this.operaciones();
    switch (this.filtro()) {
      case 'sin-factura':
        return l.filter(o => o.factura.estado === EstadoDePata.Pendiente);
      case 'sin-transaccion':
        return l.filter(o => o.transaccion.estado === EstadoDePata.Pendiente);
      case 'sin-control':
        return l.filter(o => o.controlInterno.estado === EstadoDePata.Pendiente);
      case 'completas':
        return l.filter(o => o.respondidas === 3);
      default:
        return l;
    }
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.pararAlDia());
  }

  ngOnInit(): void {
    this.perfiles.clientes().subscribe({
      next: lista => {
        this.clientes.set(lista);
        const pedido = this.ruta.snapshot.queryParamMap.get('cliente');
        const elegido = lista.find(c => c.id === pedido) ?? (lista.length === 1 ? lista[0] : null);
        if (elegido) {
          this.elegir(elegido.id);
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string | null): void {
    this.clienteId.set(id);
    this.mesa.set(null);
    this.amarre.set(null);
    this.polizas.set(null);
    this.avisosDeAnalisis.set([]);
    this.router.navigate([], { queryParams: { cliente: id }, replaceUrl: true });
    this.cargar();
  }

  cambiarMes(): void {
    this.amarre.set(null);
    this.avisosDeAnalisis.set([]);
    this.cargar();
  }

  cargar(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.error.set(null);
    this.api.delMes(id, this.anio, this.mes).subscribe({
      next: m => this.mesa.set(m),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la mesa de trabajo.')),
    });
    this.api.polizas(id, this.anio, this.mes).subscribe({ next: p => this.ponerPolizas(p), error: () => this.ponerPolizas(null) });
    this.cargarAlDia();
    this.electronica.set(null);
    this.api.apertura(id).subscribe({ next: a => this.apertura.set(a), error: () => this.apertura.set(null) });
  }

  generarPolizas(): void {
    const id = this.clienteId();
    if (!id || this.generando()) {
      return;
    }
    this.generando.set(true);
    this.error.set(null);
    const cierre = { inventarioInicial: this.inventarioInicial, inventarioFinal: this.inventarioFinal, costoVendido: this.costoVendido };
    this.api.generarPolizas(id, this.anio, this.mes, cierre).subscribe({
      next: p => {
        this.ponerPolizas(p);
        this.generando.set(false);
      },
      error: e => {
        this.generando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudieron generar las pólizas.'));
      },
    });
  }

  /**
   * Cómo va la contabilidad al día. Mientras está por actualizar se vuelve a preguntar poco después de la hora en que
   * toca; cuando termina, se recargan la mesa y las pólizas (pudo conciliar, amarrar y generar).
   */
  cargarAlDia(): void {
    this.pararAlDia();
    const id = this.clienteId();
    const anio = this.anio;
    const mes = this.mes;
    if (!id) {
      this.alDia.set(null);
      return;
    }
    this.api.contabilidadAlDia(id, anio, mes).subscribe({
      next: r => {
        // Llegó tarde la respuesta de otro cliente o mes: no se pinta.
        if (id !== this.clienteId() || anio !== this.anio || mes !== this.mes) {
          return;
        }
        const antes = this.alDia();
        this.alDia.set(r);
        if (antes?.actualizando && !r.actualizando && antes.anio === anio && antes.mes === mes) {
          this.api.delMes(id, anio, mes).subscribe({ next: m => this.mesa.set(m) });
          this.api.polizas(id, anio, mes).subscribe({ next: p => this.ponerPolizas(p) });
        }
        if (r.actualizando) {
          const falta = r.seActualizaUtc ? new Date(r.seActualizaUtc).getTime() - Date.now() + 70_000 : 0;
          this.relojAlDia = setTimeout(() => this.cargarAlDia(), Math.max(20_000, falta));
        }
      },
      error: () => this.alDia.set(null),
    });
  }

  /** «Actualizar ahora»: el servidor lo pone al día en la siguiente vuelta (alrededor de un minuto). */
  actualizarContabilidad(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.error.set(null);
    this.api.actualizarContabilidad(id, this.anio, this.mes).subscribe({
      next: r => {
        this.alDia.set(r);
        this.pararAlDia();
        this.relojAlDia = setTimeout(() => this.cargarAlDia(), 20_000);
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo pedir la actualización.')),
    });
  }

  hora(utc: string): string {
    return new Date(utc).toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' });
  }

  private pararAlDia(): void {
    if (this.relojAlDia) {
      clearTimeout(this.relojAlDia);
      this.relojAlDia = null;
    }
  }

  private ponerPolizas(p: PolizasDelMesDto | null): void {
    this.polizas.set(p);
    this.inventarioInicial = p?.cierre?.inventarioInicial ?? p?.inventarioInicialSugerido ?? null;
    this.inventarioFinal = p?.cierre?.inventarioFinal ?? null;
    this.costoVendido = p?.cierre?.costoVendido ?? null;
  }

  editarApertura(): void {
    const a = this.apertura();
    this.anioApertura = a?.anio ?? this.anio;
    this.mesApertura = a?.mes ?? this.mes;
    this.textoApertura = (a?.saldos ?? []).map(s => `${s.cuenta}\t${s.deudor || ''}\t${s.acreedor || ''}`).join('\n');
    this.editandoApertura.set(true);
  }

  guardarApertura(): void {
    const id = this.clienteId();
    const leida = this.aperturaLeida;
    if (!id || leida.malos) {
      return;
    }
    this.api.guardarApertura(id, { anio: this.anioApertura, mes: this.mesApertura, saldos: leida.saldos }).subscribe({
      next: a => {
        this.apertura.set(a);
        this.editandoApertura.set(false);
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudieron guardar los saldos de apertura.')),
    });
  }

  generarElectronica(): void {
    const id = this.clienteId();
    if (!id || this.generandoXml()) {
      return;
    }
    this.generandoXml.set(true);
    this.error.set(null);
    this.api.contabilidadElectronica(id, this.anio, this.mes).subscribe({
      next: r => {
        this.electronica.set(r);
        this.generandoXml.set(false);
      },
      error: e => {
        this.generandoXml.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo generar la contabilidad electrónica.'));
      },
    });
  }

  descargarXml(a: { nombre: string; contenidoB64: string }): void {
    const bytes = Uint8Array.from(atob(a.contenidoB64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/xml' }));
    const enlace = document.createElement('a');
    enlace.href = url;
    enlace.download = a.nombre;
    enlace.click();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  }

  /** «Sí, está bien»: se acepta lo que detenía la operación y se vuelve a generar. */
  aceptarRevision(r: RenglonDeRevisionDto): void {
    const id = this.clienteId();
    if (!id || !r.operacion) {
      return;
    }
    this.api.aceptarRevision(id, r.operacion, r.codigo).subscribe({
      next: () => this.generarPolizas(),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo aceptar.')),
    });
  }

  /** Cómo se llama la operación de un renglón de la revisión o de una póliza. */
  nombreDe(operacionId: string | null): string {
    if (!operacionId) {
      return 'Todo el mes';
    }
    const o = this.operaciones().find(x => x.id === operacionId);
    return o ? `${o.fecha.substring(8, 10)}/${o.fecha.substring(5, 7)} · ${o.contraparte || o.descripcion}` : 'Operación';
  }

  abrirOperacion(operacionId: string | null): void {
    if (operacionId) {
      this.filtro.set('todas');
      this.abierta.set(operacionId);
      document.getElementById('ms-operaciones')?.scrollIntoView({ behavior: 'smooth' });
    }
  }

  /** De tres en tres, hasta que no quede nada por leer (o Claude no esté disponible). */
  analizar(): void {
    const id = this.clienteId();
    if (!id || this.analizando()) {
      return;
    }
    const avisos: string[] = [];
    let leidos = 0;
    const vuelta = () => {
      this.analizando.set(`Claude está leyendo… (${leidos} listos)`);
      this.api.analizar(id, this.anio, this.mes).subscribe({
        next: r => {
          leidos += r.analizados;
          avisos.push(...r.avisos);
          this.avisosDeAnalisis.set([...avisos]);
          if (r.faltan > 0 && r.analizados > 0) {
            vuelta();
          } else {
            this.analizando.set(null);
            this.cargar();
          }
        },
        error: e => {
          this.analizando.set(null);
          this.error.set(mensajeDeError(e, 'No se pudo analizar.'));
          this.cargar();
        },
      });
    };
    vuelta();
  }

  amarrar(): void {
    const id = this.clienteId();
    if (!id || this.amarrando()) {
      return;
    }
    this.amarrando.set(true);
    this.error.set(null);
    this.api.amarrarBanco(id, this.anio, this.mes).subscribe({
      next: r => {
        this.amarre.set(r);
        this.amarrando.set(false);
        this.cargar();
      },
      error: e => {
        this.amarrando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo amarrar el banco.'));
      },
    });
  }

  aceptar(s: SugerenciaDeAmarreDto): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.operacionesApi.juntar(id, s.operacionId, s.movimientoId).subscribe({
      next: () => {
        this.quitar(s);
        this.cargar();
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudieron juntar.')),
    });
  }

  quitar(s: SugerenciaDeAmarreDto): void {
    this.amarre.update(r => (r ? { ...r, sugerencias: r.sugerencias.filter(x => x !== s) } : r));
  }

  abrir(o: OperacionDto): void {
    this.abierta.set(this.abierta() === o.id ? null : o.id);
  }

  cambio(o: OperacionDto | null): void {
    this.abierta.set(o?.id ?? null);
    this.cargar();
  }

  circulo(e: EstadoDePata): string {
    return e === EstadoDePata.Ligada ? 'ms-circulo ms-ligada' : e === EstadoDePata.NoHay ? 'ms-circulo ms-nohay' : 'ms-circulo ms-pendiente';
  }

  /** La categoría que dio Claude al control interno o al comprobante, si la hay. */
  categoria(o: OperacionDto): string | null {
    const c = o.controlInterno.analisis?.relacionConGiro ?? o.transaccion.analisis?.relacionConGiro;
    return c && c !== 'no_aplica' ? (this.categorias[c] ?? c) : null;
  }
}
