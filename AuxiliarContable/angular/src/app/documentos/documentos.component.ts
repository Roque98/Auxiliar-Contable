import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, EstadoDeDocumento, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { chatEncendido } from '../shared/chat';
import {
  DocumentoDeBovedaDto,
  DocumentosService,
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
  imports: [FormsModule, RouterLink],
  templateUrl: './documentos.component.html',
  styleUrls: ['./documentos.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentosComponent implements OnInit {
  private readonly api = inject(DocumentosService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly Tipo = TipoDeDocumento;
  readonly Estado = EstadoDeDocumento;
  readonly nombresDeTipo = NOMBRES_DE_TIPO;
  readonly meses = MESES;
  readonly tiposParaSubir = [TipoDeDocumento.EstadoDeCuenta, TipoDeDocumento.Ticket, TipoDeDocumento.Otro];
  readonly tiposParaFiltrar = [TipoDeDocumento.Cfdi, TipoDeDocumento.EstadoDeCuenta, TipoDeDocumento.Ticket,
    TipoDeDocumento.ConstanciaDeSituacionFiscal, TipoDeDocumento.Otro];

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
  tipo: TipoDeDocumento | null = null;
  texto = '';
  tipoAlSubir = TipoDeDocumento.EstadoDeCuenta;
  /** A qué mes van (menos las facturas, que van al de su XML). */
  anioAlSubir = 0;
  mesAlSubir = 0;
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
   * Un estado de cuenta llega al mes siguiente del que cubre: se sugiere el mes pasado. Un ticket u
   * otro documento suele ser de este mes. Quien sube lo puede cambiar.
   */
  sugerirMes(): void {
    const hoy = new Date();
    const atras = this.tipoAlSubir === TipoDeDocumento.EstadoDeCuenta ? 1 : 0;
    const d = new Date(hoy.getFullYear(), hoy.getMonth() - atras, 1);
    this.anioAlSubir = d.getFullYear();
    this.mesAlSubir = d.getMonth() + 1;
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
    this.buscar();
  }

  elegirMes(mes: number | null): void {
    this.mes.set(this.mes() === mes ? null : mes);
    this.buscar();
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

  /** Uno por uno: cada archivo con su resultado, y un error no detiene a los demás. */
  async subir(evento: Event): Promise<void> {
    const entrada = evento.target as HTMLInputElement;
    const archivos = Array.from(entrada.files ?? []).slice(0, 50);
    entrada.value = '';
    const id = this.clienteId();
    if (!id || !archivos.length) {
      return;
    }
    this.error.set(null);
    this.subidas.set(archivos.map(a => ({ nombre: a.name, estado: 'subiendo' as const })));
    for (let i = 0; i < archivos.length; i++) {
      const resultado = await new Promise<Subida>(resolver =>
        this.api.subir(id, this.tipoAlSubir, archivos[i], this.anioAlSubir, this.mesAlSubir).subscribe({
          next: r => resolver({
            nombre: archivos[i].name,
            estado: r.aviso ? 'aviso' : r.esDuplicado ? 'duplicado' : 'ok',
            mensaje: r.aviso ?? (r.esDuplicado ? 'Ya estaba en la bóveda.' : undefined),
          }),
          error: e => resolver({ nombre: archivos[i].name, estado: 'error', mensaje: mensajeDeError(e, 'No se pudo subir.') }),
        }),
      );
      this.subidas.update(s => s.map((x, j) => (j === i ? resultado : x)));
    }
    this.recargar();
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
        return 'bi-ticket-perforated';
      case TipoDeDocumento.ConstanciaDeSituacionFiscal:
        return 'bi-person-lines-fill';
      default:
        return d.tipoMime.startsWith('image/') ? 'bi-file-earmark-image' : 'bi-file-earmark';
    }
  }
}
