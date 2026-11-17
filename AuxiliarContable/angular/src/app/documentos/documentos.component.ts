import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, EstadoDeDocumento, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { chatEncendido } from '../shared/chat';
import { OperacionDto, OperacionesService } from '../mi-informacion/operaciones.service';
import { PatasComponent } from '../mi-informacion/patas.component';
import {
  DocumentoDeBovedaDto,
  DocumentosService,
  FacturaClasificadaDto,
  FacturasClasificadasDto,
  MESES,
  NOMBRES_DE_TIPO,
  ResumenDelMesDto,
  TipoDeDocumento,
} from './documentos.service';

interface Subida {
  nombre: string;
  estado: 'subiendo' | 'ok' | 'duplicado' | 'aviso' | 'error';
  mensaje?: string;
}

interface Mes {
  anio: number;
  mes: number;
  cantidad: number;
  porTipo: { tipo: TipoDeDocumento; cantidad: number }[];
}

const POR_PAGINA = 20;

/**
 * La bóveda de documentos de un cliente: subir (varios a la vez), navegar año → mes → tipo,
 * buscar y descargar. El cliente activo va en la URL (`?cliente=`) y se ve arriba.
 *
 * <p>Mobile first: una columna, tarjetas y acciones de 44 px; desde 768 px el resumen de meses
 * va en rejilla.</p>
 */
@Component({
  selector: 'app-documentos',
  standalone: true,
  imports: [FormsModule, RouterLink, PatasComponent],
  templateUrl: './documentos.component.html',
  styleUrls: ['./documentos.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentosComponent implements OnInit {
  private readonly api = inject(DocumentosService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly operaciones = inject(OperacionesService);

  /** Dentro de «Mi información», la pestaña: solo esa zona de subida (y sus documentos). Sin ella, todas. */
  readonly zona = (this.ruta.snapshot.data['zona'] ?? null) as 'facturas' | 'dinero' | 'control' | null;
  /** Lo recién subido, con su operación: se pregunta por las otras dos patas (se puede dejar para después). */
  readonly porAmarrar = signal<OperacionDto[]>([]);
  private readonly router = inject(Router);

  readonly Tipo = TipoDeDocumento;
  readonly Estado = EstadoDeDocumento;
  readonly nombresDeTipo = NOMBRES_DE_TIPO;
  readonly meses = MESES;
  readonly categorias = CATEGORIAS;
  readonly tiposParaFiltrar = [TipoDeDocumento.Cfdi, TipoDeDocumento.EstadoDeCuenta, TipoDeDocumento.ComprobanteDePago,
    TipoDeDocumento.ControlInterno, TipoDeDocumento.Ticket, TipoDeDocumento.ConstanciaDeSituacionFiscal, TipoDeDocumento.Otro];

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  /** Sin chat (sin API de Claude) no se ofrece «preguntar al asistente». */
  readonly chat = chatEncendido();
  readonly clienteId = signal<string | null>(null);
  readonly resumen = signal<ResumenDelMesDto[]>([]);
  readonly documentos = signal<DocumentoDeBovedaDto[] | null>(null);
  readonly total = signal(0);
  readonly subidas = signal<Subida[]>([]);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal<string | null>(null);

  readonly anio = signal<number | null>(null);
  readonly mes = signal<number | null>(null);
  tipo: TipoDeDocumento | null =
    this.zona === 'facturas' ? TipoDeDocumento.Cfdi
    : this.zona === 'dinero' ? TipoDeDocumento.EstadoDeCuenta
    : this.zona === 'control' ? TipoDeDocumento.ControlInterno
    : null;
  texto = '';
  /** Estados de cuenta y dinero: qué es (estado de cuenta o comprobante de transferencia/pago). */
  tipoDeDinero = TipoDeDocumento.EstadoDeCuenta;
  /** A qué mes van (las facturas van al de su XML; el estado de cuenta, si se deja en 0, al que lea el motor). */
  anioAlSubir = 0;
  mesAlSubir = 0;
  anioDeControl = 0;
  mesDeControl = 0;
  /** «Mis facturas» del mes elegido: se piden con el botón (el motor lee cada XML). */
  readonly clasificadas = signal<FacturasClasificadasDto | null>(null);
  readonly clasificando = signal(false);
  readonly lado = signal<'recibida' | 'emitida'>('recibida');
  readonly abierta = signal<string | null>(null);
  readonly grupos = computed(() => {
    const c = this.clasificadas();
    if (!c) {
      return [];
    }
    const porCategoria = new Map<string, FacturaClasificadaDto[]>();
    for (const f of c.facturas.filter(x => x.lado === this.lado())) {
      porCategoria.set(f.categoria, [...(porCategoria.get(f.categoria) ?? []), f]);
    }
    return [...porCategoria.entries()]
      .map(([categoria, facturas]) => ({ categoria, facturas, total: facturas.reduce((s, f) => s + (f.total ?? 0), 0) }))
      .sort((a, b) => (CATEGORIAS[a.categoria]?.orden ?? 99) - (CATEGORIAS[b.categoria]?.orden ?? 99));
  });
  readonly porDefinir = computed(() => this.clasificadas()?.facturas.filter(f => f.estadoDePago === 'por_definir').length ?? 0);
  readonly aniosParaSubir = [0, 1, 2].map(n => new Date().getFullYear() - n);

  /** El documento al que se le está cambiando el mes, y el mes elegido. */
  readonly editando = signal<string | null>(null);
  anioEditado = 0;
  mesEditado = 0;

  readonly cliente = computed(() => this.clientes()?.find(c => c.id === this.clienteId()) ?? null);
  readonly anios = computed(() => [...new Set(this.resumen().map(r => r.anio))].sort((a, b) => b - a));
  readonly mesesDelAnio = computed<Mes[]>(() => {
    const anio = this.anio();
    const porMes = new Map<number, Mes>();
    for (const r of this.resumen().filter(x => x.anio === anio)) {
      const m = porMes.get(r.mes) ?? { anio: r.anio, mes: r.mes, cantidad: 0, porTipo: [] };
      m.cantidad += r.cantidad;
      m.porTipo.push({ tipo: r.tipo, cantidad: r.cantidad });
      porMes.set(r.mes, m);
    }
    return [...porMes.values()].sort((a, b) => b.mes - a.mes);
  });
  readonly hayMas = computed(() => (this.documentos()?.length ?? 0) < this.total());

  constructor() {
    this.sugerirMes();
  }

  /**
   * Un estado de cuenta se acomoda solo: el motor lee su periodo (mes 0 = «que lo lea»). Un ticket u
   * otro documento suele ser de este mes. Quien sube lo puede cambiar.
   */
  sugerirMes(): void {
    const hoy = new Date();
    this.anioAlSubir = this.anioDeControl = hoy.getFullYear();
    this.mesAlSubir = this.tipoDeDinero === TipoDeDocumento.EstadoDeCuenta ? 0 : hoy.getMonth() + 1;
    this.mesDeControl = hoy.getMonth() + 1;
  }

  get leerElMes(): boolean {
    return this.tipoDeDinero === TipoDeDocumento.EstadoDeCuenta && this.mesAlSubir === 0;
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
    this.resumen.set([]);
    this.documentos.set(null);
    this.subidas.set([]);
    this.anio.set(null);
    this.mes.set(null);
    this.router.navigate([], { queryParams: { cliente: id }, replaceUrl: true });
    if (id) {
      this.recargar();
    }
  }

  recargar(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.api.resumen(id).subscribe({
      next: r => {
        this.resumen.set(r);
        if (this.anio() === null && r.length) {
          this.anio.set(Math.max(...r.map(x => x.anio)));
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar el resumen.')),
    });
    this.buscar();
  }

  elegirAnio(anio: number): void {
    this.anio.set(anio);
    this.mes.set(null);
    this.clasificadas.set(null);
    this.buscar();
  }

  elegirMes(mes: number | null): void {
    this.mes.set(this.mes() === mes ? null : mes);
    this.clasificadas.set(null);
    this.buscar();
  }

  /** «Mis facturas» del mes elegido, clasificadas por el motor con el giro del perfil. */
  verFacturas(): void {
    const id = this.clienteId();
    const anio = this.anio();
    const mes = this.mes();
    if (!id || !anio || !mes || this.clasificando()) {
      return;
    }
    this.clasificando.set(true);
    this.error.set(null);
    this.api.clasificarFacturas(id, anio, mes).subscribe({
      next: c => {
        if (id === this.clienteId() && anio === this.anio() && mes === this.mes()) {
          this.clasificadas.set(c);
        }
        this.clasificando.set(false);
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudieron clasificar las facturas.'));
        this.clasificando.set(false);
      },
    });
  }

  nombreDeCategoria(c: string | null): string {
    return (c && CATEGORIAS[c]?.nombre) || 'Sin clasificar';
  }

  buscar(mas = false): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    const ya = mas ? (this.documentos() ?? []) : [];
    this.api
      .buscar({
        organizacionId: id,
        anio: this.mes() !== null || this.texto.trim() === '' ? this.anio() : null,
        mes: this.mes(),
        tipo: this.tipo,
        texto: this.texto.trim() || null,
        skipCount: ya.length,
        maxResultCount: POR_PAGINA,
      })
      .subscribe({
        next: r => {
          this.total.set(r.totalCount);
          this.documentos.set([...ya, ...r.items]);
        },
        error: e => this.error.set(mensajeDeError(e, 'No se pudieron cargar los documentos.')),
      });
  }

  /**
   * Uno por uno: cada archivo con su resultado, y un error no detiene a los demás. Cada zona decide qué es lo que se
   * sube: facturas (el XML como factura y su PDF aparte), estados de cuenta y dinero, o control interno.
   */
  async subir(evento: Event, zona: 'facturas' | 'dinero' | 'control'): Promise<void> {
    const entrada = evento.target as HTMLInputElement;
    const archivos = Array.from(entrada.files ?? []).slice(0, 50);
    entrada.value = '';
    const id = this.clienteId();
    if (!id || !archivos.length) {
      return;
    }
    this.error.set(null);
    this.subidas.set(archivos.map(a => ({ nombre: a.name, estado: 'subiendo' as const })));
    this.porAmarrar.set([]);
    for (let i = 0; i < archivos.length; i++) {
      const resultado = await new Promise<Subida>(resolver =>
        this.api.subir(id, ...this.destino(zona, archivos[i])).subscribe({
          next: r => {
            this.abrirOperacion(id, r.documento.id, r.documento.tipo);
            resolver({
              nombre: archivos[i].name,
              estado: r.aviso ? 'aviso' : r.esDuplicado ? 'duplicado' : 'ok',
              mensaje: r.aviso ?? (r.esDuplicado ? 'Ya estaba en la bóveda.' : undefined),
            });
          },
          error: e => resolver({ nombre: archivos[i].name, estado: 'error', mensaje: mensajeDeError(e, 'No se pudo subir.') }),
        }),
      );
      this.subidas.update(s => s.map((x, j) => (j === i ? resultado : x)));
    }
    this.recargar();
  }

  /**
   * La factura (XML), el comprobante de pago o el control interno recién subido abren (o encuentran) su operación,
   * para preguntar enseguida por las otras dos patas. Un estado de cuenta no: sus movimientos se amarran en «Todo».
   */
  private abrirOperacion(organizacionId: string, documentoId: string, tipo: TipoDeDocumento): void {
    if (![TipoDeDocumento.Cfdi, TipoDeDocumento.ComprobanteDePago, TipoDeDocumento.ControlInterno].includes(tipo) || this.porAmarrar().length >= 10) {
      return;
    }
    this.operaciones.deDocumento(organizacionId, documentoId).subscribe({
      next: o => this.porAmarrar.update(l => (l.some(x => x.id === o.id) ? l : [...l, o])),
      // Si no se pudo, el documento ya está subido: se amarra después en «Todo».
      error: () => undefined,
    });
  }

  /** Lo que cambió al amarrar: se reemplaza (o se quita si se borró o se juntó con otra). */
  amarrada(antes: OperacionDto, despues: OperacionDto | null): void {
    this.porAmarrar.update(l => l
      .map(x => (x.id === antes.id ? despues : x))
      .filter((x, i, todos): x is OperacionDto => !!x && todos.findIndex(y => y?.id === x.id) === i));
  }

  /** Tipo y mes con que se sube un archivo según la zona. */
  private destino(zona: 'facturas' | 'dinero' | 'control', archivo: File): [TipoDeDocumento, File, number?, number?] {
    if (zona === 'facturas') {
      // El XML se acomoda solo en el mes de su fecha; el PDF de la factura va como «otro» en el mes actual.
      return /\.xml$/i.test(archivo.name) || archivo.type.includes('xml')
        ? [TipoDeDocumento.Cfdi, archivo]
        : [TipoDeDocumento.Otro, archivo];
    }
    if (zona === 'dinero') {
      return [this.tipoDeDinero, archivo, this.anioAlSubir, this.mesAlSubir];
    }
    return [TipoDeDocumento.ControlInterno, archivo, this.anioDeControl, this.mesDeControl];
  }

  descargar(d: DocumentoDeBovedaDto): void {
    this.ocupado.set(d.id);
    this.api.descargar(this.clienteId()!, d.id).subscribe({
      next: blob => {
        this.ocupado.set(null);
        const url = URL.createObjectURL(blob);
        const enlace = document.createElement('a');
        enlace.href = url;
        enlace.download = d.nombre;
        enlace.click();
        setTimeout(() => URL.revokeObjectURL(url), 10_000);
      },
      error: e => {
        this.ocupado.set(null);
        this.error.set(mensajeDeError(e, 'No se pudo descargar.'));
      },
    });
  }

  eliminar(d: DocumentoDeBovedaDto): void {
    if (this.ocupado() === `borrar:${d.id}`) {
      // Segundo toque: confirmado (sin diálogos del navegador).
      this.api.eliminar(this.clienteId()!, d.id).subscribe({
        next: () => {
          this.ocupado.set(null);
          this.recargar();
        },
        error: e => {
          this.ocupado.set(null);
          this.error.set(mensajeDeError(e, 'No se pudo borrar.'));
        },
      });
      return;
    }
    this.ocupado.set(`borrar:${d.id}`);
  }

  editar(d: DocumentoDeBovedaDto): void {
    const f = new Date(d.fecha);
    this.anioEditado = f.getUTCFullYear();
    this.mesEditado = f.getUTCMonth() + 1;
    this.editando.set(d.id);
  }

  guardarMes(d: DocumentoDeBovedaDto): void {
    this.ocupado.set(d.id);
    this.api.cambiarMes(this.clienteId()!, d.id, this.anioEditado, this.mesEditado).subscribe({
      next: () => {
        this.ocupado.set(null);
        this.editando.set(null);
        this.recargar();
      },
      error: e => {
        this.ocupado.set(null);
        this.error.set(mensajeDeError(e, 'No se pudo cambiar el mes.'));
      },
    });
  }

  /** De cuándo es: el día de una factura; de lo demás, el mes al que se asignó. */
  periodo(d: DocumentoDeBovedaDto): string {
    if (d.tipo === TipoDeDocumento.Cfdi) {
      return this.fecha(d.fecha);
    }
    const f = new Date(d.fecha);
    return `${MESES[f.getUTCMonth()]} ${f.getUTCFullYear()}`;
  }

  fecha(iso: string): string {
    return new Date(iso).toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  icono(d: DocumentoDeBovedaDto): string {
    switch (d.tipo) {
      case TipoDeDocumento.Cfdi:
        return 'bi-receipt';
      case TipoDeDocumento.EstadoDeCuenta:
        return 'bi-bank';
      case TipoDeDocumento.Ticket:
      case TipoDeDocumento.ControlInterno:
        return 'bi-ticket-perforated';
      case TipoDeDocumento.ComprobanteDePago:
        return 'bi-arrow-left-right';
      case TipoDeDocumento.ConstanciaDeSituacionFiscal:
        return 'bi-person-lines-fill';
      default:
        return d.tipoMime.startsWith('image/') ? 'bi-file-earmark-image' : 'bi-file-earmark';
    }
  }
}

/** Cómo se nombra cada categoría de «Mis facturas», en el orden en que se enseñan. */
const CATEGORIAS: Record<string, { nombre: string; clase: string; orden: number }> = {
  mercancia: { nombre: 'Mercancía para revender', clase: 'ig-etiqueta-exito', orden: 1 },
  insumo_directo: { nombre: 'Materia prima o insumo', clase: 'ig-etiqueta-exito', orden: 2 },
  gasto_indirecto: { nombre: 'Gasto indirecto', clase: 'ig-etiqueta-tinta', orden: 3 },
  gasto_operacion: { nombre: 'Gasto de operación', clase: 'ig-etiqueta-tinta', orden: 4 },
  inversion: { nombre: 'Inversión (activo fijo)', clase: 'ig-etiqueta-acento', orden: 5 },
  deduccion_personal: { nombre: 'Deducción personal', clase: 'ig-etiqueta-acento', orden: 6 },
  ajeno: { nombre: 'Ajeno al giro', clase: 'ig-etiqueta-error', orden: 7 },
  sin_clasificar: { nombre: 'Sin clasificar (falta el giro en el perfil)', clase: 'ig-etiqueta-alerta', orden: 8 },
  venta: { nombre: 'Venta', clase: 'ig-etiqueta-exito', orden: 10 },
  nota_de_credito: { nombre: 'Nota de crédito', clase: 'ig-etiqueta-tinta', orden: 11 },
  complemento_de_pago: { nombre: 'Complemento de pago', clase: 'ig-etiqueta-tinta', orden: 12 },
  nomina: { nombre: 'Nómina', clase: 'ig-etiqueta-tinta', orden: 13 },
  traslado: { nombre: 'Traslado', clase: 'ig-etiqueta-tinta', orden: 14 },
};
