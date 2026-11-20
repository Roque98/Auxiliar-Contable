import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { RenglonesComponent } from '../mesa/renglones.component';
import { RepseComponent } from './repse.component';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { Periodicidad, finDelPeriodo, siguienteInicio, soloDia } from './fechas';
import { ArchivosDeIdseDto, ArchivosDeSuaDto, CuotasDelMesDto, MovimientoAfiliatorioDto, NominaDelClienteDto, NominaService, PeriodoDeNominaDto, RecibosDeNominaDto, TrabajadorDto } from './nomina.service';
import { descargarBase64 } from '../mesa/descarga';
import { MESES } from '../documentos/documentos.service';

/** Columnas de incidencias que se capturan por trabajador. */
const COLUMNAS: { clave: 'faltas' | 'incapacidad' | 'horasSemana' | 'horasExtraDobles' | 'horasExtraTriples' | 'domingosTrabajados' | 'diasDescansoTrabajados' | 'festivosTrabajados' | 'aguinaldo' | 'primaVacacional'; titulo: string }[] = [
  { clave: 'faltas', titulo: 'Faltas' },
  { clave: 'incapacidad', titulo: 'Incapacidad (días)' },
  { clave: 'horasSemana', titulo: 'Horas a la semana' },
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
  imports: [FormsModule, CurrencyPipe, DatePipe, DecimalPipe, RenglonesComponent, RepseComponent],
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
  /** Lo que Claude leyó de la lista de raya: qué documentos, dudas y notas (las incidencias quedan en la tabla sin guardar). */
  readonly lectura = signal<{ documentos: string[]; dudas: string[]; notas: string[]; leidos: string[] } | null>(null);
  readonly editando = signal<TrabajadorDto | null>(null);
  readonly abierto = signal<string | null>(null);
  nuevoInicio = '';
  periodicidad: Periodicidad = 'quincenal';
  readonly meses = MESES;
  readonly idse = signal<ArchivosDeIdseDto | null>(null);
  readonly recibos = signal<RecibosDeNominaDto | null>(null);
  readonly cuotas = signal<CuotasDelMesDto | null>(null);
  readonly sua = signal<ArchivosDeSuaDto | null>(null);
  mesCuotas = new Date().getMonth() + 1;
  causaBaja = '2';
  readonly causas = [
    ['1', 'Término de contrato'], ['2', 'Separación voluntaria'], ['3', 'Abandono de empleo'], ['4', 'Defunción'], ['5', 'Clausura'],
    ['6', 'Otras'], ['7', 'Ausentismo'], ['8', 'Rescisión de contrato'], ['9', 'Jubilación'], ['A', 'Pensión'],
  ];
  readonly textoTipo: Record<string, string> = { alta: 'Alta o reingreso (08)', modificacion: 'Modificación de salario (07)', baja: 'Baja (02)' };

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
    this.editando.set({ ...t, fechaIngreso: t.fechaIngreso.slice(0, 10), creditoInfonavitInicio: soloDia(t.creditoInfonavitInicio) });
  }

  guardarTrabajador(): void {
    const t = this.editando();
    const id = this.clienteId();
    if (!t || !id) {
      return;
    }
    this.hacer(this.api.guardarTrabajador(id, { ...t, creditoInfonavitInicio: soloDia(t.creditoInfonavitInicio) }), () => {
      this.editando.set(null);
      this.cargar();
    }, 'No se pudo guardar el trabajador.');
  }

  baja(t: TrabajadorDto, darDeBaja: boolean): void {
    const id = this.clienteId();
    if (!id || !t.id) {
      return;
    }
    this.hacer(this.api.darDeBaja(id, t.id, darDeBaja ? new Date().toISOString().slice(0, 10) : null, this.causaBaja), () => this.cargar(),
      'No se pudo cambiar el estado.');
  }

  nombreCompleto(t: TrabajadorDto): string {
    return [t.nombre, t.primerApellido, t.segundoApellido].filter(x => !!x).join(' ');
  }

  pendientes(): MovimientoAfiliatorioDto[] {
    return (this.nomina()?.movimientos ?? []).filter(m => !m.presentadoUtc);
  }

  /** Arma los archivos del IDSE con lo pendiente y los descarga. */
  archivosIdse(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.hacer(this.api.archivosIdse(id, []), r => {
      this.idse.set(r);
      for (const a of r.archivos) {
        descargarBase64(a.nombre, a.contenidoB64, 'text/plain');
      }
    }, 'No se pudieron armar los archivos del IDSE.');
  }

  marcarPresentados(): void {
    const id = this.clienteId();
    const r = this.idse();
    if (!id || !r) {
      return;
    }
    const conError = new Set(r.errores.map(e => e.id));
    const ids = this.pendientes().map(m => m.id).filter(x => !conError.has(x));
    this.hacer(this.api.marcarPresentados(id, ids), () => {
      this.idse.set(null);
      this.cargar();
    }, 'No se pudieron marcar.');
  }

  descargarRecibos(): void {
    const id = this.clienteId();
    const p = this.periodo();
    if (!id || !p) {
      return;
    }
    this.hacer(this.api.recibos(id, p.id), r => {
      this.recibos.set(r);
      descargarBase64(`recibos de nomina ${p.inicio.slice(0, 10)} al ${p.fin.slice(0, 10)}.zip`, r.zipB64, 'application/zip');
    }, 'No se pudieron armar los recibos.');
  }

  verCuotas(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.hacer(this.api.cuotasDelMes(id, this.anio, this.mesCuotas), r => this.cuotas.set(r), 'No se pudieron sumar las cuotas.');
  }

  /** Archivos para importar en el SUA del mes elegido, en un .zip. */
  archivosSua(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    const mes = this.mesCuotas;
    this.hacer(this.api.archivosSua(id, this.anio, mes), r => {
      this.sua.set(r);
      if (r.archivos.length) {
        descargarBase64(`SUA ${this.anio}-${String(mes).padStart(2, '0')}.zip`, r.zipB64, 'application/zip');
      }
    }, 'No se pudieron armar los archivos del SUA.');
  }

  // ---------------------------------------------------------------- periodos
  crearPeriodo(): void {
    const id = this.clienteId();
    if (!id || !this.nuevoInicio) {
      return;
    }
    this.hacer(this.api.crearPeriodo(id, this.nuevoInicio, this.finNuevo(), this.periodicidad), p => {
      this.ponerPeriodo(p);
      this.cargar();
    }, 'No se pudo crear el periodo.');
  }

  abrirPeriodo(p: PeriodoDeNominaDto): void {
    this.recibos.set(null);
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.hacer(this.api.periodo(id, p.id), r => this.ponerPeriodo(r), 'No se pudo abrir el periodo.');
  }

  guardarIncidencias(): void {
    const id = this.clienteId();
    const p = this.periodo();
    if (!id || !p) {
      return;
    }
    this.hacer(this.api.guardarIncidencias(id, p.id, this.paraEnviar(p.incidencias)), r => this.ponerPeriodo(r), 'No se pudieron guardar las incidencias.');
  }

  leerIncidencias(): void {
    const id = this.clienteId();
    const p = this.periodo();
    if (!id || !p) {
      return;
    }
    this.lectura.set(null);
    this.hacer(this.api.leerIncidencias(id, p.id), r => {
      this.periodo.set({ ...p, incidencias: this.paraEnviar(r.incidencias) });
      this.lectura.set({ documentos: r.documentos, dudas: r.dudas, notas: r.notas, leidos: r.leidos });
    }, 'No se pudieron leer las incidencias de los documentos.');
  }

  calcular(): void {
    const id = this.clienteId();
    const p = this.periodo();
    if (!id || !p) {
      return;
    }
    // Lo capturado se guarda antes de calcular.
    this.hacer(this.api.guardarIncidencias(id, p.id, this.paraEnviar(p.incidencias)), () =>
      this.hacer(this.api.calcular(id, p.id), r => {
        this.ponerPeriodo(r);
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

  /** Las fechas de las incidencias, en AAAA-MM-DD para los campos de fecha. */
  private ponerPeriodo(p: PeriodoDeNominaDto): void {
    this.lectura.set(null);
    this.periodo.set({ ...p, incidencias: this.paraEnviar(p.incidencias) });
  }

  /** Fechas vacías como null (el servidor no acepta "" como fecha). */
  private paraEnviar(lista: PeriodoDeNominaDto['incidencias']): PeriodoDeNominaDto['incidencias'] {
    return lista.map(i => ({
      ...i,
      incidencias: {
        ...i.incidencias,
        inicioFaltas: soloDia(i.incidencias.inicioFaltas),
        inicioIncapacidad: soloDia(i.incidencias.inicioIncapacidad),
        folioIncapacidad: i.incidencias.folioIncapacidad?.trim() || null,
      },
    }));
  }

  /** Los trabajadores del periodo con faltas o incapacidad: el SUA pide su fecha y folio. */
  conFaltasOIncapacidad(p: PeriodoDeNominaDto): PeriodoDeNominaDto['incidencias'] {
    return p.incidencias.filter(i => this.numero(i.incidencias.faltas) || this.numero(i.incidencias.incapacidad));
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
