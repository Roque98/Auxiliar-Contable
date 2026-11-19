import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { AnalisisNormativoDto, DisparadorDto, NormativaService } from './normativa.service';
import { ResultadoNormativo, leerResultado } from './resultado';

const CAPAS: Record<string, string> = {
  ley: 'Ley', reglamento: 'Reglamento', rmf: 'Resolución Miscelánea', criterio: 'Criterio del SAT',
  otra_ley: 'Otra ley', estatal: 'Estatal o municipal',
};

/**
 * Análisis normativo del despacho: se describe una operación o pregunta de un cliente y el motor revisa el núcleo
 * fiscal y las leyes que activa su perfil, contesta los chispazos y cita su fundamento. Tarda unos minutos: se pide y
 * la lista se actualiza sola.
 */
@Component({
  selector: 'app-normativa',
  imports: [FormsModule, DatePipe],
  templateUrl: './normativa.component.html',
  styleUrls: ['../mesa/mesa.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NormativaComponent implements OnInit {
  private readonly api = inject(NormativaService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly destruir = inject(DestroyRef);

  readonly capas = CAPAS;
  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly lista = signal<AnalisisNormativoDto[] | null>(null);
  readonly abierto = signal<{ fila: AnalisisNormativoDto; resultado: ResultadoNormativo | null } | null>(null);
  readonly disparadores = signal<DisparadorDto[]>([]);
  readonly error = signal<string | null>(null);
  readonly trabajando = signal(false);

  pregunta = '';
  fecha = '';
  contraparteTipo: '' | 'fisica' | 'moral' = '';
  contraparteResidencia = '';
  marcados = new Set<string>();

  ngOnInit(): void {
    this.api.disparadores().subscribe({ next: d => this.disparadores.set(d) });
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
    // Mientras haya análisis pendientes, la lista se refresca cada 20 s.
    interval(20000).pipe(takeUntilDestroyed(this.destruir)).subscribe(() => {
      if (this.lista()?.some(a => a.estado === 'Pendiente')) {
        this.cargar();
      }
    });
  }

  elegir(id: string | null): void {
    this.clienteId.set(id);
    this.abierto.set(null);
    this.lista.set(null);
    this.cargar();
  }

  cargar(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.api.lista(id).subscribe({
      next: l => this.lista.set(l),
      error: e => this.error.set(mensajeDeError(e, 'No se pudieron cargar los análisis.')),
    });
  }

  alternar(clave: string): void {
    if (this.marcados.has(clave)) {
      this.marcados.delete(clave);
    } else {
      this.marcados.add(clave);
    }
  }

  pedir(): void {
    const id = this.clienteId();
    if (!id || this.pregunta.trim().length < 10) {
      this.error.set('Describe la operación o la pregunta con un poco más de detalle.');
      return;
    }
    this.trabajando.set(true);
    this.error.set(null);
    this.api.pedir(id, {
      pregunta: this.pregunta.trim(),
      fechaDeLaOperacion: this.fecha || null,
      contraparteTipoDePersona: this.contraparteTipo || null,
      contraparteResidencia: this.contraparteResidencia.trim() || null,
      disparadores: [...this.marcados],
    }).subscribe({
      next: () => {
        this.trabajando.set(false);
        this.pregunta = '';
        this.marcados.clear();
        this.cargar();
      },
      error: e => {
        this.trabajando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo pedir el análisis.'));
      },
    });
  }

  abrir(fila: AnalisisNormativoDto): void {
    const id = this.clienteId();
    if (!id || fila.estado !== 'Listo') {
      return;
    }
    this.api.analisis(id, fila.id).subscribe({
      next: a => this.abierto.set({ fila: a, resultado: leerResultado(a.resultado) }),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo abrir el análisis.')),
    });
  }

  cerrar(): void {
    this.abierto.set(null);
  }

  claves(o: Record<string, string[]> | null | undefined): string[] {
    return o ? Object.keys(o) : [];
  }
}
