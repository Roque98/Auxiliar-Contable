import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnChanges, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import {
  ESTADOS, HallazgoDeLeyDto, LeyDelClienteDto, LeyQueTeAplicaDto, LeyesQueTeAplicanDto, MisLeyesDto, MisLeyesService, QUE_REEMPLAZA,
  leerRespuesta,
} from './mis-leyes.service';

/** Cada cuánto se vuelve a preguntar mientras algo se lee, se analiza o se contesta. */
const ESPERA_MS = 15000;

/**
 * «Mis leyes»: el cliente sube las leyes estatales y municipales que le afectan; el motor las lee por artículos, busca lo que
 * le toca según su perfil y propone hallazgos con cita literal verificada. El despacho acepta o rechaza cada uno: lo aceptado
 * reemplaza al valor del sistema solo para este cliente (tasa y día del ISN, recargos) o aparece como obligación local.
 */
@Component({
  selector: 'app-mis-leyes',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './mis-leyes.component.html',
  styleUrls: ['./mis-leyes.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MisLeyesComponent implements OnChanges {
  private readonly api = inject(MisLeyesService);

  readonly organizacionId = input.required<string>();

  readonly estados = ESTADOS;
  readonly queReemplaza = QUE_REEMPLAZA;
  readonly leer = leerRespuesta;

  readonly datos = signal<MisLeyesDto | null>(null);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly subiendo = signal(false);
  readonly preguntando = signal(false);
  readonly ocupado = signal<string | null>(null);
  readonly aplican = signal<LeyesQueTeAplicanDto | null>(null);

  archivo: File | null = null;
  ambito: 'estatal' | 'municipal' = 'estatal';
  estado: string | null = null;
  municipio = '';
  materia = '';
  pregunta = '';

  private temporizador: ReturnType<typeof setTimeout> | null = null;

  readonly nombreDeLey = computed(() => new Map((this.datos()?.leyes ?? []).map(l => [l.id, l.titulo || l.materia || l.nombreDelArchivo])));
  readonly valores = computed(() => this.hallazgos().filter(h => h.clave));
  readonly obligaciones = computed(() => this.hallazgos().filter(h => !h.clave && (h.tipo === 'obligacion' || h.tipo === 'valor')));
  readonly beneficios = computed(() => this.hallazgos().filter(h => !h.clave && h.tipo === 'beneficio'));
  readonly avisos = computed(() => this.hallazgos().filter(h => !h.clave && h.tipo === 'aviso'));
  readonly porDecidir = computed(() => this.hallazgos().filter(h => h.decision === 'Propuesto').length);

  /** Las leyes que le tocan, por bloque; las que no existen en su estado o las regula otra autoridad van aparte. */
  readonly bloques = computed(() => {
    const a = this.aplican();
    if (!a) {
      return [];
    }
    return Object.entries(a.bloques)
      .map(([clave, titulo]) => ({ clave, titulo, leyes: a.leyes.filter(l => l.bloque === clave && (l.estatus === 'local' || l.estatus === 'pendiente' || l.estatus === 'sin_ficha')) }))
      .filter(b => b.leyes.length);
  });
  readonly otras = computed(() => (this.aplican()?.leyes ?? []).filter(l => l.estatus === 'no_existe' || l.estatus === 'federal' || l.estatus === 'municipal'));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.parar());
  }

  ngOnChanges(): void {
    this.datos.set(null);
    this.aplican.set(null);
    this.cargar();
    this.cargarQueTeAplican();
  }

  cargarQueTeAplican(): void {
    this.api.queTeAplican(this.organizacionId()).subscribe({
      next: a => this.aplican.set(a),
      error: () => this.aplican.set({ aviso: 'No se pudo consultar qué leyes te aplican.', fuentes: [], disparadores: [], bloques: {}, leyes: [] }),
    });
  }

  usar(l: LeyQueTeAplicaDto): void {
    this.ocupado.set(l.id);
    this.error.set(null);
    this.api.usarDeLaBiblioteca(this.organizacionId(), l.id).subscribe({
      next: () => { this.ocupado.set(null); this.cargar(); this.cargarQueTeAplican(); },
      error: e => { this.ocupado.set(null); this.error.set(mensajeDeError(e, 'No se pudo agregar la ley.')); },
    });
  }

  textoDeEstatus(l: LeyQueTeAplicaDto): string {
    switch (l.estatus) {
      case 'local':
        return l.enMisLeyes ? 'En tus leyes' : 'La tenemos';
      case 'pendiente':
        return 'Aún no la tenemos';
      case 'no_existe':
        return 'Tu estado no tiene esta ley';
      case 'federal':
        return 'La regula una ley federal';
      case 'municipal':
        return 'La regula tu municipio';
      default:
        return 'Súbela tú';
    }
  }

  private hallazgos(): HallazgoDeLeyDto[] {
    return this.datos()?.hallazgos ?? [];
  }

  cargar(): void {
    this.parar();
    this.cargando.set(true);
    this.api.misLeyes(this.organizacionId()).subscribe({
      next: d => {
        this.datos.set(d);
        this.cargando.set(false);
        if (this.estado == null && d.estadoDelCliente) {
          this.estado = d.estadoDelCliente;
        }
        if (d.enCurso) {
          this.temporizador = setTimeout(() => this.cargar(), ESPERA_MS);
        }
      },
      error: e => {
        this.cargando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudieron cargar tus leyes.'));
      },
    });
  }

  private parar(): void {
    if (this.temporizador) {
      clearTimeout(this.temporizador);
      this.temporizador = null;
    }
  }

  elegirArchivo(ev: Event): void {
    const f = (ev.target as HTMLInputElement).files?.[0] ?? null;
    this.archivo = f;
    if (f && !this.materia) {
      this.materia = f.name.replace(/\.pdf$/i, '').replace(/[_-]+/g, ' ');
    }
  }

  subir(campoArchivo: HTMLInputElement): void {
    if (!this.archivo) {
      return;
    }
    this.subiendo.set(true);
    this.error.set(null);
    this.api.subir(this.organizacionId(), this.archivo, this.ambito, this.estado, this.ambito === 'municipal' ? this.municipio : null,
      this.materia || null).subscribe({
      next: () => {
        this.subiendo.set(false);
        this.archivo = null;
        campoArchivo.value = '';
        this.materia = '';
        this.municipio = '';
        this.cargar();
      },
      error: e => {
        this.subiendo.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo subir la ley.'));
      },
    });
  }

  eliminar(l: LeyDelClienteDto): void {
    this.ocupado.set(l.id);
    this.api.eliminar(this.organizacionId(), l.id).subscribe({
      next: () => { this.ocupado.set(null); this.cargar(); },
      error: e => { this.ocupado.set(null); this.error.set(mensajeDeError(e, 'No se pudo quitar la ley.')); },
    });
  }

  reanalizar(l: LeyDelClienteDto): void {
    this.ocupado.set(l.id);
    this.api.reanalizar(this.organizacionId(), l.id).subscribe({
      next: () => { this.ocupado.set(null); this.cargar(); },
      error: e => { this.ocupado.set(null); this.error.set(mensajeDeError(e, 'No se pudo pedir el análisis.')); },
    });
  }

  decidir(h: HallazgoDeLeyDto, aceptar: boolean): void {
    this.ocupado.set(h.id);
    this.api.decidir(this.organizacionId(), h.id, aceptar).subscribe({
      next: () => { this.ocupado.set(null); this.cargar(); },
      error: e => { this.ocupado.set(null); this.error.set(mensajeDeError(e, 'No se pudo guardar la decisión.')); },
    });
  }

  preguntar(): void {
    const texto = this.pregunta.trim();
    if (texto.length < 8) {
      return;
    }
    this.preguntando.set(true);
    this.api.preguntar(this.organizacionId(), texto).subscribe({
      next: () => { this.preguntando.set(false); this.pregunta = ''; this.cargar(); },
      error: e => { this.preguntando.set(false); this.error.set(mensajeDeError(e, 'No se pudo hacer la pregunta.')); },
    });
  }

  nombreDelEstado(clave?: string | null): string {
    return ESTADOS.find(e => e.clave === clave)?.nombre ?? clave ?? '';
  }

  textoDeLectura(l: LeyDelClienteDto): string {
    switch (l.lectura) {
      case 'Pendiente':
        return 'En fila para leerse';
      case 'Leyendo':
        return l.paginasConOcr
          ? `Leyendo páginas escaneadas: ${l.paginasLeidas ?? 0} de ${l.paginasConOcr}`
          : 'Leyendo…';
      case 'Error':
        return 'No se pudo leer';
      default:
        return l.analisis === 'analizada' ? 'Revisada' : l.analisis === 'error' ? 'No se pudo revisar' : 'Buscando lo que te toca…';
    }
  }
}
