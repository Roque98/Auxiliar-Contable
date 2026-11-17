import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { DocumentosService, TipoDeDocumento } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import {
  ControlInternoService,
  ESTADO,
  EstadoDeEvidencia,
  ExpedienteDelMesDto,
  OperacionDto,
  TIPOS_DE_EVIDENCIA,
  TipoDeEvidencia,
} from './control-interno.service';

const MESES = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];

/** Una operación se identifica por su clave del banco o por su id de efectivo y crédito. */
function idDe(o: OperacionDto): string {
  return o.clave ?? o.movimientoFueraDelBancoId ?? '';
}

/**
 * Control interno: cada salida de dinero del mes con la evidencia que la respalda. Una factura sola no prueba que la
 * operación ocurrió (materialidad): hace falta contrato, entregable o pedido. Una salida sin factura necesita al menos
 * un comprobante o la explicación del cliente.
 */
@Component({
  selector: 'app-control-interno',
  standalone: true,
  imports: [FormsModule, CurrencyPipe],
  templateUrl: './control-interno.component.html',
  styleUrls: ['./control-interno.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ControlInternoComponent implements OnInit {
  private readonly api = inject(ControlInternoService);
  private readonly documentos = inject(DocumentosService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly meses = MESES;
  readonly tipos = TIPOS_DE_EVIDENCIA;
  readonly estado = ESTADO;
  readonly Estado = EstadoDeEvidencia;
  readonly idDe = idDe;
  readonly anios: number[];
  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly datos = signal<ExpedienteDelMesDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal(false);
  readonly soloLoQueFalta = signal(true);
  readonly verTodo = signal(false);
  /** La operación a la que se le está agregando evidencia. */
  readonly abierta = signal<string | null>(null);

  readonly visibles = computed(() => {
    const d = this.datos();
    if (!d) {
      return [];
    }
    return d.operaciones.filter(o =>
      (this.verTodo() || o.estado !== EstadoDeEvidencia.NoAplica) &&
      (!this.soloLoQueFalta() || o.estado === EstadoDeEvidencia.FaltaMaterialidad || o.estado === EstadoDeEvidencia.FaltaComprobante));
  });

  anio: number;
  mes: number;
  tipo: TipoDeEvidencia = TipoDeEvidencia.Contrato;
  nota = '';
  archivo: File | null = null;

  constructor() {
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

  cargar(): void {
    const id = this.clienteId();
    this.datos.set(null);
    this.abierta.set(null);
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
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar el control interno del mes.')),
    });
  }

  abrir(o: OperacionDto): void {
    const id = idDe(o);
    this.abierta.set(this.abierta() === id ? null : id);
    this.tipo = o.estado === EstadoDeEvidencia.FaltaComprobante ? TipoDeEvidencia.Testimonio : TipoDeEvidencia.Contrato;
    this.nota = '';
    this.archivo = null;
    this.error.set(null);
  }

  elegirArchivo(evento: Event): void {
    this.archivo = (evento.target as HTMLInputElement).files?.[0] ?? null;
  }

  nombreDeTipo(tipo: TipoDeEvidencia): string {
    return this.tipos.find(t => t.tipo === tipo)?.nombre ?? '';
  }

  agregar(o: OperacionDto): void {
    const id = this.clienteId();
    if (!id || this.ocupado() || (!this.archivo && !this.nota.trim())) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    const enviar = (documentoId: string | null) =>
      this.api.agregar(id, {
        anio: this.anio,
        mes: this.mes,
        origen: o.origen,
        clave: o.clave,
        movimientoFueraDelBancoId: o.movimientoFueraDelBancoId,
        tipo: this.tipo,
        documentoId,
        nota: this.nota.trim() || null,
      }).subscribe({
        next: () => {
          this.ocupado.set(false);
          this.cargar();
        },
        error: e => {
          this.error.set(mensajeDeError(e, 'No se pudo agregar la evidencia.'));
          this.ocupado.set(false);
        },
      });
    if (!this.archivo) {
      enviar(null);
      return;
    }
    // El archivo va a la bóveda del cliente, en el mes de la operación.
    this.documentos.subir(id, TipoDeDocumento.Otro, this.archivo, this.anio, this.mes).subscribe({
      next: r => enviar(r.documento.id),
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo subir el archivo.'));
        this.ocupado.set(false);
      },
    });
  }

  quitar(evidenciaId: string | null): void {
    const id = this.clienteId();
    if (!id || !evidenciaId || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.api.quitar(id, evidenciaId).subscribe({
      next: () => {
        this.ocupado.set(false);
        this.cargar();
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo quitar.'));
        this.ocupado.set(false);
      },
    });
  }
}
