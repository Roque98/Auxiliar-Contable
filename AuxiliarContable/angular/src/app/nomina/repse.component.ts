import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MESES } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { descargarBase64 } from '../mesa/descarga';
import { TrabajadorDto } from './nomina.service';
import { TEXTO_DEL_ESTADO, contratoNuevo, estadoDelContrato, rfcValido } from './repse';
import { CasillaRepseDto, ContratoDelMesDto, ContratoEspecializadoDto, InformeCuatrimestralDto, RepseDelMesDto, RepseService } from './repse.service';

/**
 * Servicios especializados (REPSE) de un cliente, dentro de Nómina: sus contratos (presta o recibe), la revisión del mes
 * —los documentos que hay que juntar del contratista o entregar al beneficiario, con su plazo— y el informe cuatrimestral
 * al IMSS (ICSOE) y al INFONAVIT (SISUB) con la hoja de datos para capturarlo.
 */
@Component({
  selector: 'app-repse',
  imports: [FormsModule, CurrencyPipe, DatePipe],
  templateUrl: './repse.component.html',
  styleUrls: ['../mesa/mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RepseComponent {
  private readonly api = inject(RepseService);

  readonly organizacionId = input.required<string>();
  readonly trabajadores = input<TrabajadorDto[]>([]);

  readonly meses = MESES;
  readonly anios = Array.from({ length: 4 }, (_, i) => new Date().getFullYear() - i);
  anio = new Date().getFullYear();
  mes = new Date().getMonth() + 1;
  readonly datos = signal<RepseDelMesDto | null>(null);
  readonly editando = signal<ContratoEspecializadoDto | null>(null);
  readonly hoja = signal<InformeCuatrimestralDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly trabajando = signal(false);
  readonly estadoDelContrato = estadoDelContrato;
  readonly textoDelEstado = TEXTO_DEL_ESTADO;
  readonly rfcValido = rfcValido;

  constructor() {
    effect(() => {
      this.organizacionId();
      this.hoja.set(null);
      this.editando.set(null);
      this.cargar();
    });
  }

  cargar(): void {
    const id = this.organizacionId();
    this.error.set(null);
    this.api.delMes(id, this.anio, this.mes).subscribe({
      next: d => this.datos.set(d),
      error: e => this.error.set(mensajeDeError(e, 'No se pudieron cargar los servicios especializados.')),
    });
  }

  nombreDe(c: ContratoEspecializadoDto): string {
    return `${c.contraparteNombre} (${c.contraparteRfc})`;
  }

  nombreDeTrabajador(id: string): string {
    const t = this.trabajadores().find(x => x.id === id);
    return t ? [t.nombre, t.primerApellido, t.segundoApellido].filter(Boolean).join(' ') : 'Trabajador dado de baja';
  }

  nuevo(): void {
    this.editando.set(contratoNuevo());
  }

  editar(c: ContratoEspecializadoDto): void {
    this.editando.set({
      ...c, inicio: c.inicio.slice(0, 10), fin: c.fin?.slice(0, 10) ?? null,
      registroVigenteHasta: c.registroVigenteHasta?.slice(0, 10) ?? null, trabajadores: [...c.trabajadores],
    });
  }

  asignado(c: ContratoEspecializadoDto, id: string): boolean {
    return c.trabajadores.includes(id);
  }

  asignar(c: ContratoEspecializadoDto, id: string, si: boolean): void {
    c.trabajadores = si ? [...new Set([...c.trabajadores, id])] : c.trabajadores.filter(x => x !== id);
  }

  guardar(): void {
    const c = this.editando();
    if (!c) {
      return;
    }
    this.trabajando.set(true);
    this.api.guardarContrato(this.organizacionId(), { ...c, fin: c.fin || null, registroVigenteHasta: c.registroVigenteHasta || null }).subscribe({
      next: () => {
        this.trabajando.set(false);
        this.editando.set(null);
        this.cargar();
      },
      error: e => {
        this.trabajando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo guardar el contrato.'));
      },
    });
  }

  borrar(c: ContratoEspecializadoDto): void {
    if (!c.id) {
      return;
    }
    this.trabajando.set(true);
    this.api.borrarContrato(this.organizacionId(), c.id).subscribe({
      next: () => {
        this.trabajando.set(false);
        this.editando.set(null);
        this.cargar();
      },
      error: e => {
        this.trabajando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo borrar el contrato.'));
      },
    });
  }

  /** Marca o desmarca una casilla: del contrato en el mes, o del informe cuatrimestral (sin contrato). */
  marcar(casilla: CasillaRepseDto, cumplida: boolean, contrato: ContratoDelMesDto | null): void {
    const d = this.datos();
    if (!d) {
      return;
    }
    const inf = d.informe;
    const m = contrato
      ? { contratoId: contrato.contratoId, anio: d.anio, periodo: d.mes, clave: casilla.clave, cumplida }
      : { contratoId: null, anio: inf!.anio, periodo: inf!.cuatrimestre, clave: casilla.clave, cumplida };
    casilla.cumplida = cumplida;
    this.api.marcar(this.organizacionId(), m).subscribe({
      next: () => this.cargar(),
      error: e => {
        casilla.cumplida = !cumplida;
        this.error.set(mensajeDeError(e, 'No se pudo guardar.'));
      },
    });
  }

  descargarHoja(): void {
    const inf = this.datos()?.informe;
    if (!inf) {
      return;
    }
    this.trabajando.set(true);
    this.api.informeCuatrimestral(this.organizacionId(), inf.anio, inf.cuatrimestre).subscribe({
      next: h => {
        this.trabajando.set(false);
        this.hoja.set(h);
        descargarBase64(h.nombre, h.contenidoB64, 'text/csv');
      },
      error: e => {
        this.trabajando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo armar la hoja del informe.'));
      },
    });
  }
}
