import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import {
  ConcentradoService, DeudasFiscalesDto, DetalleDeRenglonDto, FacturaExplicadaDto, ObligacionFiscalDto, RenglonDelPapel,
} from './concentrado.service';
import {
  ETIQUETA_DEL_ESTADO, TEXTO_DEL_ESTADO, TEXTO_DE_ESTADO_DE_FACTURA, consejoDelMotivo, detalleDelRenglon, mesPorOmision,
} from './concentrado';

type Pestana = 'deudas_fiscales' | 'deudas_contables' | 'activos' | 'ingresos' | 'capital';

/**
 * La hoja concentrado: lo primero que ve el cliente. «Deudas fiscales» va de lo más importante (cuánto pagar de cada
 * obligación) hasta la raíz: el cálculo renglón por renglón, las facturas detrás de un renglón (consideradas, no
 * consideradas y por definir) y el porqué de cada factura con su fundamento y de dónde se tomó.
 * Las pestañas contables (deudas contables, activos, ingresos contables y capital) siguen en construcción.
 */
@Component({
  selector: 'app-concentrado',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, DatePipe, DecimalPipe],
  templateUrl: './concentrado.component.html',
  styleUrls: ['./concentrado.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConcentradoComponent implements OnInit {
  private readonly api = inject(ConcentradoService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly meses = MESES;
  readonly anios = Array.from({ length: 4 }, (_, i) => new Date().getFullYear() - i);
  readonly textoDelEstado = TEXTO_DEL_ESTADO;
  readonly etiquetaDelEstado = ETIQUETA_DEL_ESTADO;
  readonly textoDeFactura = TEXTO_DE_ESTADO_DE_FACTURA;
  readonly consejo = consejoDelMotivo;
  readonly pestanas: { clave: Pestana; nombre: string; icono: string }[] = [
    { clave: 'deudas_fiscales', nombre: 'Deudas fiscales', icono: 'bi-bank' },
    { clave: 'deudas_contables', nombre: 'Deudas contables', icono: 'bi-journal-minus' },
    { clave: 'activos', nombre: 'Activos', icono: 'bi-safe' },
    { clave: 'ingresos', nombre: 'Ingresos', icono: 'bi-graph-up-arrow' },
    { clave: 'capital', nombre: 'Capital', icono: 'bi-pie-chart' },
  ];

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  anio = mesPorOmision(new Date()).anio;
  mes = mesPorOmision(new Date()).mes;

  readonly pestana = signal<Pestana>('deudas_fiscales');
  readonly subIngresos = signal<'fiscales' | 'contables'>('fiscales');

  readonly deudas = signal<DeudasFiscalesDto | null>(null);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  // La bajada: obligación → detalle de un renglón → factura
  readonly obligacion = signal<ObligacionFiscalDto | null>(null);
  readonly detalle = signal<DetalleDeRenglonDto | null>(null);
  readonly factura = signal<FacturaExplicadaDto | null>(null);
  readonly cargandoNivel = signal(false);

  // Registrar un pago (solo el despacho)
  readonly registrando = signal(false);
  pagoImporte: number | null = null;
  pagoFecha = new Date().toISOString().substring(0, 10);
  pagoNota = '';

  readonly obligacionesFederales = computed(() => this.deudas()?.obligaciones.filter(o => o.nivel === 'federal') ?? []);
  readonly obligacionesEstatales = computed(() => this.deudas()?.obligaciones.filter(o => o.nivel === 'estatal') ?? []);

  ngOnInit(): void {
    const q = this.ruta.snapshot.queryParamMap;
    const anio = Number(q.get('anio'));
    const mes = Number(q.get('mes'));
    if (anio > 2000 && mes >= 1 && mes <= 12) {
      this.anio = anio;
      this.mes = mes;
    }
    this.perfiles.clientes().subscribe({
      next: lista => {
        this.clientes.set(lista);
        const elegido = lista.find(c => c.id === q.get('cliente')) ?? (lista.length === 1 ? lista[0] : null);
        if (elegido) {
          this.elegir(elegido.id);
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string): void {
    this.clienteId.set(id);
    void this.router.navigate([], { queryParams: { cliente: id }, queryParamsHandling: 'merge', replaceUrl: true });
    this.cargar();
  }

  cambiarMes(): void {
    void this.router.navigate([], { queryParams: { anio: this.anio, mes: this.mes }, queryParamsHandling: 'merge', replaceUrl: true });
    this.cargar();
  }

  cambiarPestana(p: Pestana): void {
    this.pestana.set(p);
    this.alResumen();
    if (p === 'ingresos' && this.subIngresos() === 'fiscales') {
      this.abrirIngresosFiscales();
    }
  }

  cambiarSubIngresos(s: 'fiscales' | 'contables'): void {
    this.subIngresos.set(s);
    this.alResumen();
    if (s === 'fiscales') {
      this.abrirIngresosFiscales();
    }
  }

  cargar(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.cargando.set(true);
    this.error.set(null);
    this.api.deudasFiscales(id, this.anio, this.mes).subscribe({
      next: d => {
        this.deudas.set(d);
        this.cargando.set(false);
        // si se estaba viendo una obligación, se refresca con las cifras nuevas
        const o = this.obligacion();
        if (o) {
          this.obligacion.set(d.obligaciones.find(x => x.clave === o.clave) ?? null);
        }
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudieron cargar tus deudas fiscales.'));
        this.cargando.set(false);
      },
    });
  }

  // ------------------------------------------------------------------ bajar y subir

  alResumen(): void {
    this.obligacion.set(null);
    this.detalle.set(null);
    this.factura.set(null);
    this.registrando.set(false);
  }

  abrirObligacion(o: ObligacionFiscalDto): void {
    this.obligacion.set(o);
    this.detalle.set(null);
    this.factura.set(null);
  }

  alaObligacion(): void {
    this.detalle.set(null);
    this.factura.set(null);
  }

  alDetalle(): void {
    this.factura.set(null);
  }

  puedeAbrir(r: RenglonDelPapel): boolean {
    return !!r.detalle && !!this.deudas()?.calculado;
  }

  abrirRenglon(r: RenglonDelPapel): void {
    const o = this.obligacion();
    const id = this.clienteId();
    const d = detalleDelRenglon(r.detalle, o?.clave ?? '', this.deudas()?.claseDelCalculo);
    if (!o || !id || !d) {
      return;
    }
    this.pedirDetalle(id, d.detalle, d.soloDelMes);
  }

  abrirIngresosFiscales(): void {
    const id = this.clienteId();
    if (id) {
      this.pedirDetalle(id, 'ingresos', true);
    }
  }

  private pedirDetalle(id: string, detalle: string, soloDelMes: boolean): void {
    this.cargandoNivel.set(true);
    this.api.detalle(id, this.anio, this.mes, detalle, soloDelMes).subscribe({
      next: d => {
        this.detalle.set(d);
        this.factura.set(null);
        this.cargandoNivel.set(false);
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo abrir el detalle.'));
        this.cargandoNivel.set(false);
      },
    });
  }

  abrirFactura(uuid: string): void {
    const id = this.clienteId();
    if (!id || !uuid) {
      return;
    }
    this.cargandoNivel.set(true);
    this.api.factura(id, this.anio, this.mes, uuid).subscribe({
      next: f => {
        this.factura.set(f);
        this.cargandoNivel.set(false);
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo abrir la factura.'));
        this.cargandoNivel.set(false);
      },
    });
  }

  // ------------------------------------------------------------------ pagos (despacho)

  empezarPago(o: ObligacionFiscalDto): void {
    this.pagoImporte = o.total > 0 ? o.total : null;
    this.pagoFecha = new Date().toISOString().substring(0, 10);
    this.pagoNota = '';
    this.registrando.set(true);
  }

  guardarPago(o: ObligacionFiscalDto): void {
    const id = this.clienteId();
    if (!id || !this.pagoImporte || this.pagoImporte <= 0) {
      return;
    }
    this.api.registrarPago(id, {
      anio: this.anio, mes: this.mes, clave: o.clave, importe: this.pagoImporte, fechaDePago: this.pagoFecha, nota: this.pagoNota || null,
    }).subscribe({
      next: () => {
        this.registrando.set(false);
        this.cargar();
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo registrar el pago.')),
    });
  }

  borrarPago(pagoId: string): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.api.borrarPago(id, pagoId).subscribe({
      next: () => this.cargar(),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo borrar el pago.')),
    });
  }

  nombreDelMes(m: number): string {
    return this.meses[m - 1] ?? '';
  }
}
