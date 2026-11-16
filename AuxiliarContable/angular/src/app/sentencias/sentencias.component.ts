import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, timer } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { A_FAVOR, EstadoDeLaLibreria, SentenciaDto, SentenciasService } from './sentencias.service';

/** Situaciones que se pueden tocar: enseñan que se busca describiendo el caso, no con la clave de la tesis. */
const EJEMPLOS = [
  'proveedor sin capacidad para dar el servicio',
  'deducción sin documentación comprobatoria',
  'devolución de saldo a favor negada',
  'multa por no presentar declaraciones',
  'notificación por buzón tributario',
];

/** Mientras corre una actualización, cada cuánto se pregunta cómo va. */
const CADA_MS = 5000;

/**
 * Librería de sentencias: tesis, precedentes y jurisprudencias fiscales del TFJA y de la SCJN, cada una con lo que
 * concluyó el tribunal, a quién favoreció, con qué fundamento y qué hacer. La búsqueda va en la URL (`?q=&fuente=`).
 * El equipo ve el botón «Actualizar» (baja lo nuevo de los sitios oficiales y lo estudia con Claude en el motor).
 */
@Component({
  selector: 'app-sentencias',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './sentencias.component.html',
  styleUrls: ['./sentencias.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SentenciasComponent implements OnInit {
  private readonly api = inject(SentenciasService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destruir = inject(DestroyRef);

  readonly ejemplos = EJEMPLOS;
  readonly resultados = signal<SentenciaDto[] | null>(null);
  readonly libreria = signal<EstadoDeLaLibreria | null>(null);
  readonly error = signal<string | null>(null);
  readonly errorDeEstado = signal<string | null>(null);
  readonly ocupado = signal(false);
  readonly abiertos = signal<Set<string>>(new Set());
  readonly hayMas = signal(false);
  readonly cargandoMas = signal(false);
  /** Lo que se buscó, para pedir la página siguiente aunque cambie el texto del buscador. */
  private ultima: { consulta: string; fuente: string | null; pagina: number } | null = null;

  consulta = '';
  fuente: string | null = null;
  private sondeo?: Subscription;

  ngOnInit(): void {
    const q = this.ruta.snapshot.queryParamMap;
    this.consulta = q.get('q') ?? '';
    this.fuente = q.get('fuente');
    this.cargarEstado();
    if (this.consulta.trim().length >= 2) {
      this.buscar();
    }
  }

  usarEjemplo(texto: string): void {
    this.consulta = texto;
    this.buscar();
  }

  buscar(): void {
    const consulta = this.consulta.trim();
    if (consulta.length < 2 || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    this.abiertos.set(new Set());
    void this.router.navigate([], { queryParams: { q: consulta, fuente: this.fuente || null }, replaceUrl: true });
    this.ultima = { consulta, fuente: this.fuente || null, pagina: 1 };
    this.api.buscar(consulta, this.fuente || null).subscribe({
      next: r => {
        this.resultados.set(r.resultados);
        this.hayMas.set(r.hayMas);
        this.ocupado.set(false);
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo buscar.'));
        this.ocupado.set(false);
      },
    });
  }

  /** «Ver más»: la página siguiente de la misma búsqueda, debajo de lo que ya se ve. */
  verMas(): void {
    const u = this.ultima;
    if (!u || this.cargandoMas()) {
      return;
    }
    this.cargandoMas.set(true);
    this.api.buscar(u.consulta, u.fuente, u.pagina + 1).subscribe({
      next: r => {
        u.pagina += 1;
        this.resultados.update(a => [...(a ?? []), ...r.resultados]);
        this.hayMas.set(r.hayMas);
        this.cargandoMas.set(false);
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudieron traer más.'));
        this.cargandoMas.set(false);
      },
    });
  }

  actualizar(): void {
    this.errorDeEstado.set(null);
    this.api.actualizar().subscribe({
      next: e => this.ponerEstado(e),
      error: e => this.errorDeEstado.set(mensajeDeError(e, 'No se pudo iniciar la actualización.')),
    });
  }

  alternar(id: string): void {
    this.abiertos.update(s => {
      const nuevo = new Set(s);
      if (nuevo.has(id)) {
        nuevo.delete(id);
      } else {
        nuevo.add(id);
      }
      return nuevo;
    });
  }

  aFavor(t: SentenciaDto) {
    return t.estudio ? A_FAVOR[t.estudio.aFavor] ?? null : null;
  }

  /** «TFJA IX-J-2aS-115 (jurisprudencia)»: lo que se pega en un escrito o en un papel de trabajo. */
  cita(t: SentenciaDto): string {
    return `${t.fuente} ${t.clave ?? ''}${t.tipo ? ` (${t.tipo})` : ''}`.replace(/\s+/g, ' ').trim();
  }

  copiarCita(t: SentenciaDto): void {
    navigator.clipboard?.writeText(`${this.cita(t)} — ${t.rubro}`).catch(() => undefined);
  }

  private cargarEstado(): void {
    this.api
      .estado()
      .pipe(takeUntilDestroyed(this.destruir))
      .subscribe({
        next: e => this.ponerEstado(e),
        error: e => this.errorDeEstado.set(mensajeDeError(e, 'No se pudo saber cuántas sentencias hay.')),
      });
  }

  /** Mientras el motor trabaja se pregunta cada pocos segundos; al terminar se deja de preguntar. */
  private ponerEstado(e: EstadoDeLaLibreria): void {
    this.libreria.set(e);
    if (e.estado.trabajo.corriendo && !this.sondeo) {
      this.sondeo = timer(CADA_MS, CADA_MS)
        .pipe(
          switchMap(() => this.api.estado()),
          takeUntilDestroyed(this.destruir),
        )
        .subscribe({
          next: s => {
            this.libreria.set(s);
            if (!s.estado.trabajo.corriendo) {
              this.sondeo?.unsubscribe();
              this.sondeo = undefined;
            }
          },
          error: () => {
            this.sondeo?.unsubscribe();
            this.sondeo = undefined;
          },
        });
    }
  }
}
