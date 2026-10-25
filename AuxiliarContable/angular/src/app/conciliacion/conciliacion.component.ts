import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { MESES } from '../documentos/documentos.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { ConciliacionDelMesDto, ConciliacionService, FacturaDelMesDto, MovimientoDelBancoDto } from './conciliacion.service';

/**
 * Conciliación de un mes: el sistema propone parejas banco ↔ factura por reglas; una persona
 * confirma, rechaza, une a mano o marca «no lleva factura». Lo que sobra son los faltantes.
 * Mobile first: secciones apiladas con tarjetas; acciones de 44 px.
 */
@Component({
  selector: 'app-conciliacion',
  standalone: true,
  imports: [FormsModule, RouterLink],
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
  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly datos = signal<ConciliacionDelMesDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal(false);
  /** Movimiento al que se le está buscando factura a mano, o marcando «sin factura». */
  readonly eligiendo = signal<MovimientoDelBancoDto | null>(null);
  readonly justificando = signal<string | null>(null);
  porque = '';

  anio: number;
  mes: number;

  readonly cliente = computed(() => this.clientes()?.find(c => c.id === this.clienteId()) ?? null);
  /** Facturas libres del mismo lado que el movimiento elegido (abono → ventas, cargo → gastos). */
  readonly facturasParaElegir = computed<FacturaDelMesDto[]>(() => {
    const m = this.eligiendo();
    const d = this.datos();
    if (!m || !d) {
      return [];
    }
    return m.abono > 0 ? d.ventasSinDeposito : d.gastosSinCargo;
  });

  constructor() {
    const hoy = new Date();
    const anterior = new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1);
    this.anio = anterior.getFullYear();
    this.mes = anterior.getMonth() + 1;
  }

  ngOnInit(): void {
    const q = this.ruta.snapshot.queryParamMap;
    this.anio = Number(q.get('anio')) || this.anio;
    this.mes = Number(q.get('mes')) || this.mes;
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

  elegir(id: string | null): void {
    this.clienteId.set(id);
    this.cargar();
  }

  cambiarMes(delta: number): void {
    const f = new Date(this.anio, this.mes - 1 + delta, 1);
    this.anio = f.getFullYear();
    this.mes = f.getMonth() + 1;
    this.cargar();
  }

  cargar(): void {
    const id = this.clienteId();
    this.datos.set(null);
    this.eligiendo.set(null);
    this.router.navigate([], { queryParams: { cliente: id, anio: this.anio, mes: this.mes }, replaceUrl: true });
    if (id) {
      this.hacer(this.api.delMes(id, this.anio, this.mes), d => this.datos.set(d));
    }
  }

  proponer(): void {
    this.hacer(this.api.proponer(this.clienteId()!, this.anio, this.mes), d => this.datos.set(d));
  }

  confirmar(id: string): void {
    this.hacer(this.api.confirmar(this.clienteId()!, id), () => this.cargar());
  }

  rechazar(id: string): void {
    this.hacer(this.api.rechazar(this.clienteId()!, id), () => this.cargar());
  }

  confirmarTodas(): void {
    const d = this.datos();
    const seguras = d?.propuestas.filter(p => p.puntaje >= 80) ?? [];
    if (!seguras.length) {
      return;
    }
    let pendientes = seguras.length;
    for (const p of seguras) {
      this.api.confirmar(this.clienteId()!, p.id).subscribe({
        next: () => --pendientes === 0 && this.cargar(),
        error: e => this.error.set(mensajeDeError(e, 'No se pudo confirmar.')),
      });
    }
  }

  unir(f: FacturaDelMesDto): void {
    const m = this.eligiendo();
    if (m) {
      this.hacer(this.api.aMano(this.clienteId()!, m.id, f.id), () => this.cargar());
    }
  }

  justificar(m: MovimientoDelBancoDto): void {
    this.justificando.set(m.id);
    this.porque = '';
  }

  guardarJustificacion(m: MovimientoDelBancoDto, porque: string | null): void {
    this.hacer(this.api.sinFactura(this.clienteId()!, m.id, porque), () => {
      this.justificando.set(null);
      this.cargar();
    });
  }

  dinero(valor: number): string {
    return valor.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' });
  }

  fecha(iso: string): string {
    return new Date(iso.substring(0, 10) + 'T12:00:00').toLocaleDateString('es-MX', { day: '2-digit', month: 'short' });
  }

  monto(m: MovimientoDelBancoDto): string {
    return m.abono > 0 ? '+' + this.dinero(m.abono) : '−' + this.dinero(m.cargo);
  }

  private hacer<T>(peticion: Observable<T>, despues: (r: T) => void): void {
    this.error.set(null);
    this.ocupado.set(true);
    peticion.subscribe({
      next: r => {
        this.ocupado.set(false);
        despues(r);
      },
      error: e => {
        this.ocupado.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo completar.'));
      },
    });
  }
}
