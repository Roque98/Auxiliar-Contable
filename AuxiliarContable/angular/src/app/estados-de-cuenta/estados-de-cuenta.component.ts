import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import {
  EstadoDeCuentaDto,
  EstadoDeCuentaResumenDto,
  EstadosDeCuentaService,
  MovimientoBancarioDto,
  RevisionDelEstadoDeCuenta,
} from './estados-de-cuenta.service';

/**
 * Revisión humana de los estados de cuenta leídos: el código dice si cuadra; aquí una persona
 * corrige el renglón mal leído (el que «salta» de saldo) o los saldos, y lo confirma.
 * Mobile first: lista de tarjetas → detalle con renglones en tarjetas; desde 768 px, tabla.
 */
@Component({
  selector: 'app-estados-de-cuenta',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './estados-de-cuenta.component.html',
  styleUrls: ['./estados-de-cuenta.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EstadosDeCuentaComponent implements OnInit {
  private readonly api = inject(EstadosDeCuentaService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly Revision = RevisionDelEstadoDeCuenta;

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly lista = signal<EstadoDeCuentaResumenDto[] | null>(null);
  readonly estado = signal<EstadoDeCuentaDto | null>(null);
  readonly editando = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly guardando = signal(false);
  borrador: MovimientoBancarioDto | null = null;
  saldoInicial: number | null = null;
  saldoFinal: number | null = null;

  readonly cliente = computed(() => this.clientes()?.find(c => c.id === this.clienteId()) ?? null);

  ngOnInit(): void {
    this.perfiles.clientes().subscribe({
      next: lista => {
        this.clientes.set(lista);
        const pedido = this.ruta.snapshot.queryParamMap.get('cliente');
        const elegido = lista.find(c => c.id === pedido) ?? (lista.length === 1 ? lista[0] : null);
        if (elegido) {
          this.elegir(elegido.id, this.ruta.snapshot.queryParamMap.get('estado'));
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string | null, abrir: string | null = null): void {
    this.clienteId.set(id);
    this.lista.set(null);
    this.estado.set(null);
    this.router.navigate([], { queryParams: { cliente: id }, replaceUrl: true });
    if (!id) {
      return;
    }
    this.api.lista(id).subscribe({
      next: l => {
        this.lista.set(l);
        if (abrir) {
          this.abrir(abrir);
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudieron cargar los estados de cuenta.')),
    });
  }

  abrir(estadoId: string): void {
    this.error.set(null);
    this.api.obtener(this.clienteId()!, estadoId).subscribe({
      next: e => this.mostrar(e),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo abrir el estado de cuenta.')),
    });
  }

  cerrar(): void {
    this.estado.set(null);
    this.elegir(this.clienteId());
  }

  editar(m: MovimientoBancarioDto): void {
    this.editando.set(m.id);
    this.borrador = { ...m, fecha: m.fecha.substring(0, 10) };
  }

  guardarRenglon(): void {
    const e = this.estado();
    if (!e || !this.borrador) {
      return;
    }
    this.accion(this.api.corregirMovimiento(this.clienteId()!, e.id, this.borrador));
  }

  guardarSaldos(): void {
    const e = this.estado();
    if (e) {
      this.accion(this.api.corregirSaldos(this.clienteId()!, e.id, this.saldoInicial, this.saldoFinal));
    }
  }

  confirmar(aunqueNoCuadre: boolean): void {
    const e = this.estado();
    if (e) {
      this.accion(this.api.confirmar(this.clienteId()!, e.id, aunqueNoCuadre));
    }
  }

  volverALeer(): void {
    const e = this.estado();
    if (!e) {
      return;
    }
    this.api.volverALeer(this.clienteId()!, e.documentoId).subscribe({
      next: () => {
        this.error.set('Se volverá a leer en unos segundos. Las correcciones a mano se pierden.');
        this.cerrar();
      },
      error: x => this.error.set(mensajeDeError(x, 'No se pudo volver a leer.')),
    });
  }

  dinero(valor: number | null | undefined, moneda = 'MXN'): string {
    return valor == null ? '—' : valor.toLocaleString('es-MX', { style: 'currency', currency: moneda.length === 3 ? moneda : 'MXN' });
  }

  fecha(iso: string | null | undefined): string {
    return iso ? new Date(iso.substring(0, 10) + 'T12:00:00').toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' }) : '—';
  }

  etiqueta(e: EstadoDeCuentaResumenDto): string {
    switch (e.revision) {
      case RevisionDelEstadoDeCuenta.Confirmado:
        return 'Confirmado';
      case RevisionDelEstadoDeCuenta.PorConfirmar:
        return 'Cuadra · por confirmar';
      default:
        return e.diferencia == null ? 'Faltan saldos' : `No cuadra (${this.dinero(e.diferencia, e.moneda)})`;
    }
  }

  private accion(peticion: ReturnType<EstadosDeCuentaService['obtener']>): void {
    this.error.set(null);
    this.guardando.set(true);
    peticion.subscribe({
      next: e => {
        this.guardando.set(false);
        this.mostrar(e);
      },
      error: x => {
        this.guardando.set(false);
        this.error.set(mensajeDeError(x, 'No se pudo guardar.'));
      },
    });
  }

  private mostrar(e: EstadoDeCuentaDto): void {
    this.estado.set(e);
    this.editando.set(null);
    this.borrador = null;
    this.saldoInicial = e.saldoInicial ?? null;
    this.saldoFinal = e.saldoFinal ?? null;
  }
}
