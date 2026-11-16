import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ArticuloDeLeyDto, LEYES, LeyesService } from './leyes.service';

/** Ejemplos que se pueden tocar: enseñan que sirve tanto la cita como la pregunta en palabras. */
const EJEMPLOS = ['artículo 27 LISR', 'deducción de automóviles', 'plazo de la declaración anual', 'tasa 0 % IVA alimentos'];

/**
 * Consultar leyes: el índice del motor fiscal (leyes, reglamentos, RMF y NIF) sin IA de por
 * medio. Cada resultado trae ley, artículo y última reforma para citarlo. La búsqueda va en la
 * URL (`?q=&ley=`) para poder compartirla.
 *
 * <p>Mobile first: buscador arriba y resultados apilados; el texto largo se despliega.</p>
 */
@Component({
  selector: 'app-leyes',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './leyes.component.html',
  styleUrls: ['./leyes.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LeyesComponent implements OnInit {
  private readonly api = inject(LeyesService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly leyes = LEYES;
  readonly ejemplos = EJEMPLOS;
  readonly resultados = signal<ArticuloDeLeyDto[] | null>(null);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal(false);
  readonly hayMas = signal(false);
  readonly cargandoMas = signal(false);
  /** Lo que se buscó, para pedir la página siguiente aunque cambie el texto del buscador. */
  private ultima: { consulta: string; ley: string | null; pagina: number } | null = null;
  /** Índices de los resultados con el texto completo abierto. */
  readonly abiertos = signal<Set<number>>(new Set());

  consulta = '';
  ley: string | null = null;

  ngOnInit(): void {
    const q = this.ruta.snapshot.queryParamMap;
    this.consulta = q.get('q') ?? '';
    this.ley = q.get('ley');
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
    void this.router.navigate([], { queryParams: { q: consulta, ley: this.ley || null }, replaceUrl: true });
    this.ultima = { consulta, ley: this.ley || null, pagina: 1 };
    this.api.buscar(consulta, this.ley || null).subscribe({
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
    this.api.buscar(u.consulta, u.ley, u.pagina + 1).subscribe({
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

  alternar(i: number): void {
    this.abiertos.update(s => {
      const nuevo = new Set(s);
      if (nuevo.has(i)) {
        nuevo.delete(i);
      } else {
        nuevo.add(i);
      }
      return nuevo;
    });
  }

  /** «Art. 27 LISR»: lo que se pega en un correo o en un papel de trabajo. */
  cita(a: ArticuloDeLeyDto): string {
    return a.articulo ? `Art. ${a.articulo} ${a.ley}` : a.ordenamiento;
  }

  copiarCita(a: ArticuloDeLeyDto): void {
    const texto = `${this.cita(a)} — ${a.ordenamiento}${a.ultimaReformaDof ? ` (última reforma DOF ${a.ultimaReformaDof})` : ''}`;
    navigator.clipboard?.writeText(texto).catch(() => undefined);
  }
}
