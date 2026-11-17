import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { EstadoDePata, OperacionDto, OperacionesDelMesDto, OperacionesService, SentidoDeOperacion } from './operaciones.service';
import { PatasComponent } from './patas.component';

type Filtro = 'todas' | 'faltan' | 'completas';

/**
 * «Mi información → Todo»: cada operación del mes con sus tres círculos (factura · transacción · control interno),
 * las más completas primero. Al abrir una se contesta lo que le falta.
 */
@Component({
  selector: 'app-todo',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, DatePipe, PatasComponent],
  templateUrl: './todo.component.html',
  styleUrls: ['./todo.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TodoComponent implements OnInit {
  private readonly api = inject(OperacionesService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly meses = MESES;
  readonly Estado = EstadoDePata;
  readonly Sentido = SentidoDeOperacion;
  readonly anios = Array.from({ length: 4 }, (_, i) => new Date().getFullYear() - i);

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly cliente = computed(() => this.clientes()?.find(c => c.id === this.clienteId()) ?? null);
  anio = new Date().getFullYear();
  mes = new Date().getMonth() + 1;

  readonly datos = signal<OperacionesDelMesDto | null>(null);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly filtro = signal<Filtro>('todas');
  readonly abierta = signal<string | null>(null);

  // Nueva operación a mano (p. ej. un gasto sin nada subido todavía).
  readonly creando = signal(false);
  nuevaDescripcion = '';
  nuevoImporte: number | null = null;
  nuevoSentido = SentidoDeOperacion.Salida;
  nuevaFecha = new Date().toISOString().substring(0, 10);

  readonly visibles = computed(() => {
    const lista = this.datos()?.operaciones ?? [];
    switch (this.filtro()) {
      case 'faltan':
        return lista.filter(o => o.respondidas < 3);
      case 'completas':
        return lista.filter(o => o.respondidas === 3);
      default:
        return lista;
    }
  });

  readonly cuentas = computed(() => {
    const lista = this.datos()?.operaciones ?? [];
    return {
      todas: lista.length,
      faltan: lista.filter(o => o.respondidas < 3).length,
      completas: lista.filter(o => o.respondidas === 3).length,
    };
  });

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
    this.datos.set(null);
    this.router.navigate([], { queryParams: { cliente: id }, queryParamsHandling: 'merge', replaceUrl: true });
    this.cargar();
  }

  cargar(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.cargando.set(true);
    this.error.set(null);
    this.api.delMes(id, this.anio, this.mes).subscribe({
      next: d => {
        this.datos.set(d);
        this.cargando.set(false);
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudieron cargar las operaciones.'));
        this.cargando.set(false);
      },
    });
  }

  abrir(o: OperacionDto): void {
    this.abierta.set(this.abierta() === o.id ? null : o.id);
  }

  /** Lo que cambió en una operación: se recarga el mes (pudo juntarse con otra o cambiar de lugar). */
  cambio(o: OperacionDto | null): void {
    this.abierta.set(o?.id ?? null);
    this.cargar();
  }

  crear(): void {
    const id = this.clienteId();
    if (!id || !this.nuevaDescripcion.trim()) {
      return;
    }
    this.api
      .crear(id, {
        fecha: this.nuevaFecha,
        sentido: this.nuevoSentido,
        importe: this.nuevoImporte && this.nuevoImporte > 0 ? this.nuevoImporte : null,
        descripcion: this.nuevaDescripcion.trim(),
        contraparte: null,
      })
      .subscribe({
        next: o => {
          this.creando.set(false);
          this.nuevaDescripcion = '';
          this.nuevoImporte = null;
          const f = new Date(o.fecha);
          this.anio = f.getUTCFullYear();
          this.mes = f.getUTCMonth() + 1;
          this.abierta.set(o.id);
          this.cargar();
        },
        error: e => this.error.set(mensajeDeError(e, 'No se pudo crear la operación.')),
      });
  }

  circulo(e: EstadoDePata): string {
    return e === EstadoDePata.Ligada ? 'td-circulo td-ligada' : e === EstadoDePata.NoHay ? 'td-circulo td-nohay' : 'td-circulo td-pendiente';
  }

  /** Lo que se lee de corrido en el renglón: qué hay en cada pata. */
  resumen(o: OperacionDto): string {
    const partes = [
      o.factura.estado === EstadoDePata.Ligada ? (o.factura.referencia ? 'Factura' : 'Factura solicitada')
        : o.factura.estado === EstadoDePata.NoHay ? o.factura.motivo : 'falta factura',
      o.transaccion.estado === EstadoDePata.Ligada
        ? (o.transaccion.forma === 1 ? 'Efectivo' : o.transaccion.forma === 2 ? 'Crédito' : `Banco${o.transaccion.referencia ? ' ' + o.transaccion.referencia : ''}`)
        : o.transaccion.estado === EstadoDePata.NoHay ? o.transaccion.motivo : 'falta transacción',
      o.controlInterno.estado === EstadoDePata.Ligada ? (o.controlInterno.nombreDelDocumento ?? 'Control interno')
        : o.controlInterno.estado === EstadoDePata.NoHay ? o.controlInterno.motivo : 'falta control interno',
    ];
    return partes.join(' · ');
  }
}
