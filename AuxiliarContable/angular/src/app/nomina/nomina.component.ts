import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { RenglonesComponent } from '../mesa/renglones.component';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { Periodicidad, finDelPeriodo, siguienteInicio } from './fechas';
import { NominaDelClienteDto, NominaService, PeriodoDeNominaDto, TrabajadorDto } from './nomina.service';

/** Columnas de incidencias que se capturan por trabajador. */
const COLUMNAS: { clave: 'faltas' | 'incapacidad' | 'horasExtraDobles' | 'horasExtraTriples' | 'domingosTrabajados' | 'diasDescansoTrabajados' | 'festivosTrabajados' | 'aguinaldo' | 'primaVacacional'; titulo: string }[] = [
  { clave: 'faltas', titulo: 'Faltas' },
  { clave: 'incapacidad', titulo: 'Incapacidad (días)' },
  { clave: 'horasExtraDobles', titulo: 'Horas extra dobles' },
  { clave: 'horasExtraTriples', titulo: 'Horas extra triples' },
  { clave: 'domingosTrabajados', titulo: 'Domingos trabajados' },
  { clave: 'diasDescansoTrabajados', titulo: 'Descansos trabajados' },
  { clave: 'festivosTrabajados', titulo: 'Festivos trabajados' },
  { clave: 'aguinaldo', titulo: 'Aguinaldo ($)' },
  { clave: 'primaVacacional', titulo: 'Prima vacacional ($)' },
];

function nuevoTrabajador(): TrabajadorDto {
  return { id: null, nombre: '', fechaIngreso: new Date().toISOString().slice(0, 10), salarioDiario: 0, diasAguinaldo: 15, primaVacacionalPct: 25 };
}

/** Nómina del despacho: trabajadores de un cliente y cada periodo con incidencias y su cálculo. */
@Component({
  selector: 'app-nomina',
  imports: [FormsModule, CurrencyPipe, DatePipe, DecimalPipe, RenglonesComponent],
  templateUrl: './nomina.component.html',
  styleUrls: ['../mesa/mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NominaComponent implements OnInit {
  private readonly api = inject(NominaService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);

  readonly columnas = COLUMNAS;
  readonly anios = Array.from({ length: 4 }, (_, i) => new Date().getFullYear() - i);
  anio = new Date().getFullYear();
  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly nomina = signal<NominaDelClienteDto | null>(null);
  readonly periodo = signal<PeriodoDeNominaDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly trabajando = signal(false);
  readonly editando = signal<TrabajadorDto | null>(null);
  readonly abierto = signal<string | null>(null);
  nuevoInicio = '';
  periodicidad: Periodicidad = 'quincenal';

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
    this.periodo.set(null);
    this.cargar();
  }

  cargar(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.error.set(null);
    this.api.delCliente(id, this.anio).subscribe({
      next: n => {
        this.nomina.set(n);
        const ultimo = n.periodos[0];
        this.nuevoInicio = ultimo ? siguienteInicio(ultimo.fin.slice(0, 10)) : `${this.anio}-${String(new Date().getMonth() + 1).padStart(2, '0')}-01`;
        if (ultimo) {
          this.periodicidad = ultimo.periodicidad as Periodicidad;
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la nómina.')),
    });
  }

  finNuevo(): string {
    return this.nuevoInicio ? finDelPeriodo(this.nuevoInicio, this.periodicidad) : '';
  }

  activos(): TrabajadorDto[] {
    return (this.nomina()?.trabajadores ?? []).filter(t => !t.fechaBaja);
  }

  // ---------------------------------------------------------------- trabajadores
  nuevo(): void {
    this.editando.set(nuevoTrabajador());
  }

  editar(t: TrabajadorDto): void {
    this.editando.set({ ...t, fechaIngreso: t.fechaIngreso.slice(0, 10) });
  }

  guardarTrabajador(): void {
    const t = this.editando();
    const id = this.clienteId();
    if (!t || !id) {
      return;
    }
    this.hacer(this.api.guardarTrabajador(id, t), () => {
      this.editando.set(null);
      this.cargar();
    }, 'No se pudo guardar el trabajador.');
  }

  baja(t: TrabajadorDto, darDeBaja: boolean): void {
    const id = this.clienteId();
    if (!id || !t.id) {
      return;
    }
    this.hacer(this.api.darDeBaja(id, t.id, darDeBaja ? new Date().toISOString().slice(0, 10) : null), () => this.cargar(), 'No se pudo cambiar el estado.');
  }

  // ---------------------------------------------------------------- periodos
  crearPeriodo(): void {
    const id = this.clienteId();
    if (!id || !this.nuevoInicio) {
      return;
    }
    this.hacer(this.api.crearPeriodo(id, this.nuevoInicio, this.finNuevo(), this.periodicidad), p => {
      this.periodo.set(p);
      this.cargar();
    }, 'No se pudo crear el periodo.');
  }

  abrirPeriodo(p: PeriodoDeNominaDto): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.hacer(this.api.periodo(id, p.id), r => this.periodo.set(r), 'No se pudo abrir el periodo.');
  }

  guardarIncidencias(): void {
    const id = this.clienteId();
    const p = this.periodo();
    if (!id || !p) {
      return;
    }
    this.hacer(this.api.guardarIncidencias(id, p.id, p.incidencias), r => this.periodo.set(r), 'No se pudieron guardar las incidencias.');
  }

  calcular(): void {
    const id = this.clienteId();
    const p = this.periodo();
    if (!id || !p) {
      return;
    }
    // Lo capturado se guarda antes de calcular.
    this.hacer(this.api.guardarIncidencias(id, p.id, p.incidencias), () =>
      this.hacer(this.api.calcular(id, p.id), r => {
        this.periodo.set(r);
        this.cargar();
      }, 'No se pudo calcular la nómina.'), 'No se pudieron guardar las incidencias.');
  }

  borrar(): void {
    const id = this.clienteId();
    const p = this.periodo();
    if (!id || !p) {
      return;
    }
    this.hacer(this.api.borrarPeriodo(id, p.id), () => {
      this.periodo.set(null);
      this.cargar();
    }, 'No se pudo borrar el periodo.');
  }

  /** Para la tabla de incidencias: el valor de una columna como número (vacío = 0). */
  numero(v: unknown): number {
    const n = Number(v);
    return Number.isFinite(n) && n > 0 ? n : 0;
  }

  private hacer<T>(peticion: import('rxjs').Observable<T>, ok: (r: T) => void, mensaje: string): void {
    this.trabajando.set(true);
    this.error.set(null);
    peticion.subscribe({
      next: r => {
        this.trabajando.set(false);
        ok(r);
      },
      error: e => {
        this.trabajando.set(false);
        this.error.set(mensajeDeError(e, mensaje));
      },
    });
  }
}
