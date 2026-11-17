import { PermissionService } from '@abp/ng.core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { DocumentosService, TipoDeDocumento } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import {
  ConceptoDto,
  CrearSolicitudDto,
  EstadoDeSolicitud,
  FORMAS_DE_PAGO,
  FacturarService,
  REGIMENES,
  ReceptorDto,
  SolicitudDto,
  SolicitudesDelClienteDto,
  TASAS,
  UNIDADES,
  USOS_CFDI,
} from './facturar.service';
import { OperacionDto, OperacionesService } from '../mi-informacion/operaciones.service';
import { PatasComponent } from '../mi-informacion/patas.component';

const ESTADOS: Record<EstadoDeSolicitud, { texto: string; clase: string }> = {
  [EstadoDeSolicitud.Pendiente]: { texto: 'Pendiente', clase: 'ig-etiqueta-alerta' },
  [EstadoDeSolicitud.Emitida]: { texto: 'Emitida', clase: 'ig-etiqueta-exito' },
  [EstadoDeSolicitud.Rechazada]: { texto: 'Rechazada', clase: 'ig-etiqueta-error' },
  [EstadoDeSolicitud.Cancelada]: { texto: 'Cancelada', clase: 'ig-etiqueta-tinta' },
};

function redondea(n: number): number {
  return Math.round(n * 100) / 100;
}

/**
 * Pedir factura: el cliente captura a quién y qué facturar (o sube la constancia del receptor para llenar sus datos) y
 * el despacho la timbra y deja el XML y el PDF en la bóveda, o la rechaza diciendo por qué. La pantalla calcula
 * subtotal, IVA y total para enseñarlos, pero los que valen son los que calcula el servidor.
 */
@Component({
  selector: 'app-facturar',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, DatePipe, PatasComponent],
  templateUrl: './facturar.component.html',
  styleUrls: ['./facturar.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FacturarComponent implements OnInit {
  private readonly api = inject(FacturarService);
  private readonly documentos = inject(DocumentosService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly operaciones = inject(OperacionesService);
  /** La solicitud recién enviada, como operación: se pregunta cómo se cobra y su control interno. */
  readonly porAmarrar = signal<OperacionDto | null>(null);
  /** El despacho (ve a todos los clientes): solo él ve la cola y atiende. */
  private readonly esEquipo = inject(PermissionService).getGrantedPolicy('AuxiliarContable.Organizaciones');

  readonly usos = USOS_CFDI;
  readonly formas = FORMAS_DE_PAGO;
  readonly regimenes = REGIMENES;
  readonly unidades = UNIDADES;
  readonly tasas = TASAS;
  readonly estados = ESTADOS;
  readonly Estado = EstadoDeSolicitud;
  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly datos = signal<SolicitudesDelClienteDto | null>(null);
  readonly cola = signal<SolicitudDto[] | null>(null);
  readonly error = signal<string | null>(null);
  readonly aviso = signal<string | null>(null);
  readonly ocupado = signal(false);
  readonly pidiendo = signal(false);
  /** La solicitud que el despacho está atendiendo o rechazando. */
  readonly atendiendo = signal<{ id: string; modo: 'atender' | 'rechazar' } | null>(null);

  // formulario de la solicitud
  receptor: ReceptorDto = this.receptorVacio();
  usoCfdi = 'G03';
  formaDePago = '03';
  metodoDePago = 'PUE';
  conceptos: ConceptoDto[] = [this.conceptoVacio()];
  incluyeIva = false;
  nota = '';
  // atender / rechazar
  xml: File | null = null;
  pdf: File | null = null;
  respuesta = '';

  ngOnInit(): void {
    if (this.esEquipo) {
      this.cargarCola();
    }
    const q = this.ruta.snapshot.queryParamMap;
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
    this.pidiendo.set(false);
    this.cargar();
  }

  cargar(): void {
    const id = this.clienteId();
    this.datos.set(null);
    this.atendiendo.set(null);
    if (id) {
      void this.router.navigate([], { queryParams: { cliente: id }, replaceUrl: true });
      this.api.delCliente(id).subscribe({
        next: d => {
          if (id === this.clienteId()) {
            this.datos.set(d);
            if (d.esEquipo) {
              this.cargarCola();
            }
          }
        },
        error: e => this.error.set(mensajeDeError(e, 'No se pudieron cargar las solicitudes.')),
      });
    }
  }

  private cargarCola(): void {
    this.api.pendientes().subscribe({ next: c => this.cola.set(c), error: () => this.cola.set(null) });
  }

  // ---- pedir

  nueva(): void {
    this.receptor = this.receptorVacio();
    this.usoCfdi = 'G03';
    this.formaDePago = '03';
    this.metodoDePago = 'PUE';
    this.conceptos = [this.conceptoVacio()];
    this.incluyeIva = false;
    this.nota = '';
    this.error.set(null);
    this.aviso.set(null);
    this.pidiendo.set(true);
  }

  usarReceptor(rfc: string): void {
    const r = this.datos()?.receptores.find(x => x.rfc === rfc);
    if (r) {
      this.receptor = { ...r };
    }
  }

  async leerConstancia(evento: Event): Promise<void> {
    const archivo = (evento.target as HTMLInputElement).files?.[0];
    const id = this.clienteId();
    if (!archivo || !id) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    const base64 = await this.base64(archivo);
    this.api.leerConstancia(id, base64).subscribe({
      next: r => {
        this.receptor = { ...r };
        this.ocupado.set(false);
        this.aviso.set('Se llenaron los datos con su constancia: revísalos.');
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo leer la constancia.'));
        this.ocupado.set(false);
      },
    });
  }

  agregarConcepto(): void {
    this.conceptos = [...this.conceptos, this.conceptoVacio()];
  }

  quitarConcepto(i: number): void {
    this.conceptos = this.conceptos.filter((_, j) => j !== i);
  }

  /** El precio sin IVA: si la persona escribió el precio final, se le quita el IVA de su tasa. */
  precioSinIva(c: ConceptoDto): number {
    return this.incluyeIva && c.tasaIva ? redondea(c.precioUnitario / (1 + c.tasaIva)) : c.precioUnitario;
  }

  importe(c: ConceptoDto): number {
    return redondea((c.cantidad || 0) * this.precioSinIva(c));
  }

  ivaDe(c: ConceptoDto): number {
    return c.tasaIva ? redondea(this.importe(c) * c.tasaIva) : 0;
  }

  get subtotal(): number {
    return redondea(this.conceptos.reduce((s, c) => s + this.importe(c), 0));
  }

  get iva(): number {
    return redondea(this.conceptos.reduce((s, c) => s + this.ivaDe(c), 0));
  }

  pedir(): void {
    const id = this.clienteId();
    if (!id || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    const s: CrearSolicitudDto = {
      receptor: { ...this.receptor, rfc: this.receptor.rfc.trim().toUpperCase() },
      usoCfdi: this.usoCfdi,
      formaDePago: this.metodoDePago === 'PPD' ? '99' : this.formaDePago,
      metodoDePago: this.metodoDePago,
      conceptos: this.conceptos.map(c => ({ ...c, precioUnitario: this.precioSinIva(c) })),
      nota: this.nota.trim() || null,
    };
    this.api.crear(id, s).subscribe({
      next: creada => {
        this.ocupado.set(false);
        this.operaciones.deSolicitud(id, creada.id).subscribe({ next: o => this.porAmarrar.set(o), error: () => undefined });
        this.pidiendo.set(false);
        this.aviso.set('Solicitud enviada: el despacho la timbrará y aquí verás el XML y el PDF.');
        this.cargar();
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo enviar la solicitud.'));
        this.ocupado.set(false);
      },
    });
  }

  cancelar(s: SolicitudDto): void {
    this.cambiar(this.api.cancelar(s.organizacionId, s.id), 'No se pudo cancelar.');
  }

  // ---- atender (despacho)

  abrirAtencion(s: SolicitudDto, modo: 'atender' | 'rechazar'): void {
    this.atendiendo.set({ id: s.id, modo });
    this.xml = this.pdf = null;
    this.respuesta = '';
    this.error.set(null);
  }

  elegirArchivo(evento: Event, cual: 'xml' | 'pdf'): void {
    const archivo = (evento.target as HTMLInputElement).files?.[0] ?? null;
    if (cual === 'xml') {
      this.xml = archivo;
    } else {
      this.pdf = archivo;
    }
  }

  atender(s: SolicitudDto): void {
    if (!this.xml || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    // El XML y el PDF van a la bóveda del cliente (el XML se acomoda solo en el mes de su fecha).
    forkJoin([
      this.documentos.subir(s.organizacionId, TipoDeDocumento.Cfdi, this.xml),
      this.pdf ? this.documentos.subir(s.organizacionId, TipoDeDocumento.Otro, this.pdf) : of(null),
    ]).subscribe({
      next: ([x, p]) =>
        this.cambiar(this.api.atender(s.organizacionId, s.id, x.documento.id, p?.documento.id ?? null, this.respuesta.trim() || null),
          'No se pudo marcar como emitida.'),
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudieron subir los archivos.'));
        this.ocupado.set(false);
      },
    });
  }

  rechazar(s: SolicitudDto): void {
    if (!this.respuesta.trim() || this.ocupado()) {
      return;
    }
    this.cambiar(this.api.rechazar(s.organizacionId, s.id, this.respuesta.trim()), 'No se pudo rechazar.');
  }

  irA(s: SolicitudDto): void {
    this.elegir(s.organizacionId);
  }

  nombreDe(lista: [string, string][], clave: string): string {
    return lista.find(x => x[0] === clave)?.[1] ?? clave;
  }

  private cambiar(llamada: ReturnType<FacturarService['cancelar']>, falla: string): void {
    this.ocupado.set(true);
    llamada.subscribe({
      next: () => {
        this.ocupado.set(false);
        this.cargar();
      },
      error: e => {
        this.error.set(mensajeDeError(e, falla));
        this.ocupado.set(false);
      },
    });
  }

  private receptorVacio(): ReceptorDto {
    return { rfc: '', nombre: '', codigoPostal: '', regimen: '' };
  }

  private conceptoVacio(): ConceptoDto {
    return { claveProdServ: '', descripcion: '', cantidad: 1, claveUnidad: 'H87', precioUnitario: 0, tasaIva: 0.16 };
  }

  private base64(archivo: File): Promise<string> {
    return new Promise((resolver, rechazar) => {
      const lector = new FileReader();
      lector.onload = () => resolver(String(lector.result).split(',', 2)[1] ?? '');
      lector.onerror = () => rechazar(lector.error);
      lector.readAsDataURL(archivo);
    });
  }
}
