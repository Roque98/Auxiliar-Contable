import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { MESES } from '../documentos/documentos.service';
import { CARTERA } from '../asistente/asistente.service';
import { CarteraService, ClienteDeCarteraDto, MetricasDelDespachoDto, SemaforoDeCliente } from './cartera.service';
import { NOMBRE_DE_ETAPA } from '../conciliacion/conciliacion.service';
import { chatEncendido } from '../shared/chat';

/**
 * La cartera del despacho: cada cliente con su semáforo y lo que hay que hacer, para el mes
 * elegido. Lo urgente arriba. El Owner ve además las métricas del despacho (si el servidor se
 * las da: a un Administrador le responde 403 y el bloque no sale).
 * Mobile first: tarjetas apiladas; desde 992 px, en rejilla.
 */
@Component({
  selector: 'app-cartera',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './cartera.component.html',
  styleUrls: ['./cartera.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CarteraComponent implements OnInit {
  private readonly api = inject(CarteraService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly Semaforo = SemaforoDeCliente;
  readonly meses = MESES;
  readonly cartera = CARTERA;
  readonly clientes = signal<ClienteDeCarteraDto[] | null>(null);
  /** Sin chat (sin API de Claude) no se ofrece «preguntar al asistente». */
  readonly chat = chatEncendido();
  readonly metricas = signal<MetricasDelDespachoDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly filtro = signal<SemaforoDeCliente | null>(null);
  texto = '';
  anio: number;
  mes: number;

  readonly visibles = computed(() => {
    const f = this.filtro();
    return (this.clientes() ?? []).filter(c => f === null || c.semaforo === f);
  });
  readonly cuenta = computed(() => {
    const l = this.clientes() ?? [];
    return {
      urgente: l.filter(c => c.semaforo === SemaforoDeCliente.Urgente).length,
      atencion: l.filter(c => c.semaforo === SemaforoDeCliente.Atencion).length,
      alDia: l.filter(c => c.semaforo === SemaforoDeCliente.AlDia).length,
    };
  });

  constructor() {
    const anterior = new Date(new Date().getFullYear(), new Date().getMonth() - 1, 1);
    this.anio = anterior.getFullYear();
    this.mes = anterior.getMonth() + 1;
  }

  ngOnInit(): void {
    const q = this.ruta.snapshot.queryParamMap;
    this.anio = Number(q.get('anio')) || this.anio;
    this.mes = Number(q.get('mes')) || this.mes;
    this.cargar();
  }

  cambiarMes(delta: number): void {
    const f = new Date(this.anio, this.mes - 1 + delta, 1);
    this.anio = f.getFullYear();
    this.mes = f.getMonth() + 1;
    this.cargar();
  }

  cargar(): void {
    this.clientes.set(null);
    this.error.set(null);
    this.router.navigate([], { queryParams: { anio: this.anio, mes: this.mes }, replaceUrl: true });
    this.api.clientes(this.anio, this.mes).subscribe({
      next: l => this.clientes.set(l),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la cartera.')),
    });
    this.api.metricas(this.anio, this.mes).subscribe({
      next: m => this.metricas.set(m),
      error: () => this.metricas.set(null),
    });
  }

  alternar(s: SemaforoDeCliente): void {
    this.filtro.set(this.filtro() === s ? null : s);
  }

  readonly nombreDeEtapa = NOMBRE_DE_ETAPA;

  etiqueta(s: SemaforoDeCliente): string {
    return s === SemaforoDeCliente.Urgente ? 'Urgente' : s === SemaforoDeCliente.Atencion ? 'Atención' : 'Al día';
  }

  numero(n: number): string {
    return n.toLocaleString('es-MX');
  }
}
