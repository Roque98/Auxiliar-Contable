import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ActividadDto, ActividadService, OpcionesDeActividadDto, TipoDeActividad } from './actividad.service';

interface Dia {
  titulo: string;
  items: ActividadDto[];
}

/**
 * Quién hizo qué en la consola, en frases y por día. Lo cuenta el servidor
 * (ActividadAppService) a partir de lo que ya registra; aquí solo se filtra y se pinta.
 */
@Component({
  selector: 'app-actividad',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './actividad.component.html',
  styleUrls: ['./actividad.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActividadComponent implements OnInit {
  private readonly api = inject(ActividadService);
  private readonly router = inject(Router);

  readonly tipos: { valor: TipoDeActividad | null; etiqueta: string; icono: string }[] = [
    { valor: null, etiqueta: 'Todo', icono: 'bi-list-ul' },
    { valor: 'sesion', etiqueta: 'Entradas', icono: 'bi-box-arrow-in-right' },
    { valor: 'reporte', etiqueta: 'Reportes', icono: 'bi-file-earmark-text' },
    { valor: 'organizacion', etiqueta: 'Organizaciones', icono: 'bi-building' },
    { valor: 'personas', etiqueta: 'Personas', icono: 'bi-people' },
  ];

  readonly opciones = signal<OpcionesDeActividadDto | null>(null);
  readonly items = signal<ActividadDto[]>([]);
  readonly siguiente = signal<string | null>(null);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly tipo = signal<TipoDeActividad | null>(null);
  usuarioId: string | null = null;
  organizacionId: string | null = null;

  /** Agrupado por día, en el orden en que llega (de lo más reciente a lo más antiguo). */
  readonly dias = computed<Dia[]>(() => {
    const salida: Dia[] = [];
    for (const i of this.items()) {
      const titulo = this.tituloDelDia(i.fecha);
      const ultimo = salida.at(-1);
      if (ultimo?.titulo === titulo) {
        ultimo.items.push(i);
      } else {
        salida.push({ titulo, items: [i] });
      }
    }
    return salida;
  });

  ngOnInit(): void {
    this.api.getOpciones().subscribe({ next: o => this.opciones.set(o) });
    this.buscar();
  }

  elegirTipo(t: TipoDeActividad | null): void {
    this.tipo.set(t);
    this.buscar();
  }

  buscar(): void {
    this.items.set([]);
    this.siguiente.set(null);
    this.cargar(null);
  }

  mas(): void {
    this.cargar(this.siguiente());
  }

  private cargar(antesDe: string | null): void {
    this.cargando.set(true);
    this.error.set(null);
    this.api
      .getList({ antesDe, usuarioId: this.usuarioId, organizacionId: this.organizacionId, tipo: this.tipo() })
      .subscribe({
        next: p => {
          this.items.set([...this.items(), ...p.items]);
          this.siguiente.set(p.siguienteAntesDe ?? null);
          this.cargando.set(false);
        },
        error: e => {
          this.error.set(mensajeDeError(e, 'No se pudo cargar la actividad.'));
          this.cargando.set(false);
        },
      });
  }

  icono(t: TipoDeActividad): string {
    return this.tipos.find(x => x.valor === t)?.icono ?? 'bi-dot';
  }

  hora(iso: string): string {
    return new Date(iso).toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' });
  }

  iniciales(i: ActividadDto): string {
    const base = i.nombreCompleto || i.usuario;
    return base
      .split(/[\s.]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map(p => p.charAt(0).toUpperCase())
      .join('');
  }

  private tituloDelDia(iso: string): string {
    const d = new Date(iso);
    const hoy = new Date();
    const ayer = new Date();
    ayer.setDate(hoy.getDate() - 1);
    const mismo = (a: Date, b: Date) => a.toDateString() === b.toDateString();
    if (mismo(d, hoy)) {
      return 'Hoy';
    }
    if (mismo(d, ayer)) {
      return 'Ayer';
    }
    const t = d.toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long' });
    return t.charAt(0).toUpperCase() + t.slice(1);
  }
}
