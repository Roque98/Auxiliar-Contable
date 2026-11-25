import { DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import {
  BalanceDto, CoberturaDeEstadoDto, CoberturaMunicipalDto, CoberturaMunicipalService, ExpedienteDeEstadoDto, MunicipioFueraDto,
  celdasDelMosaico, csvDeCobertura, nivelEstatal, nivelMunicipal, normasPorClase, textoDePrioridad, tramosDeBalance,
} from './cobertura-municipal.service';

type Vista = 'municipal' | 'estatal';

/**
 * Mapa de control del despacho: qué leyes estatales y qué reglamentos municipales tenemos de cada estado y municipio,
 * medidos contra el catálogo completo (tenemos / no aplica y por qué / no encontrado / sin revisar); qué municipios no se
 * eligieron y por qué; y qué clientes están fuera de cobertura. Mobile first: mapa, expediente del estado, clientes.
 */
@Component({
  selector: 'app-cobertura-municipal',
  standalone: true,
  imports: [DecimalPipe, NgTemplateOutlet],
  templateUrl: './cobertura-municipal.component.html',
  styleUrls: ['./cobertura-municipal.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CoberturaMunicipalComponent implements OnInit {
  private readonly api = inject(CoberturaMunicipalService);

  readonly datos = signal<CoberturaMunicipalDto | null>(null);
  readonly error = signal<string | null>(null);
  readonly vista = signal<Vista>('municipal');
  readonly elegido = signal('MOR');
  readonly expedientes = signal<Record<string, ExpedienteDeEstadoDto>>({});
  readonly errorExpediente = signal<string | null>(null);
  /** cvegeo del municipio abierto, o «estatal» para la ficha del estado. */
  readonly abierto = signal<string | null>(null);
  readonly avisoPrioridad = signal<string | null>(null);

  readonly celdas = computed(() => celdasDelMosaico(this.datos()?.estados ?? []));
  readonly estado = computed(() => this.datos()?.estados.find(e => e.estado === this.elegido()) ?? null);
  readonly expediente = computed(() => this.expedientes()[this.elegido()] ?? null);
  readonly estatalesCompletos = computed(() => (this.datos()?.estados ?? []).filter(e => e.estatal?.completo).length);
  readonly estatalesPendientes = computed(() => (this.datos()?.estados ?? []).reduce((a, e) => a + (e.estatal?.noEncontrado ?? 0) + (e.estatal?.sinRevisar ?? 0), 0));
  readonly sinRevisarMunicipal = computed(() => (this.datos()?.estados ?? []).reduce((a, e) => a + (e.conFicha ?? []).reduce((b, m) => b + m.sinRevisar, 0), 0));
  readonly noElegidosPorMotivo = computed(() => {
    const grupos = new Map<string, MunicipioFueraDto[]>();
    for (const m of this.estado()?.noElegidos ?? []) {
      grupos.set(m.motivo, [...(grupos.get(m.motivo) ?? []), m]);
    }
    return [...grupos.entries()].map(([motivo, municipios]) => ({ motivo, municipios }));
  });

  readonly normasPorClase = normasPorClase;
  readonly tramos = tramosDeBalance;

  ngOnInit(): void {
    this.api.reporte().subscribe({
      next: d => {
        this.datos.set(d);
        this.cargarExpediente(this.elegido());
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar el mapa de cobertura.')),
    });
  }

  elegir(estado: string): void {
    this.elegido.set(estado);
    this.abierto.set(null);
    this.cargarExpediente(estado);
  }

  alternar(clave: string): void {
    this.abierto.update(a => (a === clave ? null : clave));
  }

  nivel(e: CoberturaDeEstadoDto): number {
    return this.vista() === 'municipal' ? nivelMunicipal(e.poblacionCubiertaPct) : nivelEstatal(e.estatal);
  }

  datoDeCelda(e: CoberturaDeEstadoDto): string {
    if (this.vista() === 'municipal') {
      return `${Math.round(e.poblacionCubiertaPct ?? 0)}%`;
    }
    const b = e.estatal;
    const faltan = b ? b.noEncontrado + b.sinRevisar : 0;
    return faltan ? `faltan ${faltan}` : 'completo';
  }

  textoDeMotivo(clave: string): string {
    return this.datos()?.motivosFuera?.[clave] ?? clave;
  }

  pendientes(b: BalanceDto): number {
    return b.noEncontrado + b.sinRevisar;
  }

  textoDeCobertura(c: string): string {
    switch (c) {
      case 'no_cubierto':
        return 'Municipio no cubierto';
      case 'parcial':
        return 'Solo algunos reglamentos';
      case 'sin_fuente':
        return 'Su municipio no publica en línea';
      case 'seleccionado':
        return 'En proceso';
      default:
        return 'No se sabe su municipio';
    }
  }

  textoDeNivel(n: string | null | undefined, completo: boolean): string {
    if (completo) {
      return 'Completo';
    }
    return n === 'cubierto' ? 'Cubierto' : n === 'parcial' ? 'Parcial' : n === 'sin_fuente' ? 'Sin fuente en línea' : 'Sin ficha';
  }

  descargar(): void {
    const d = this.datos();
    if (d) {
      this.bajar(csvDeCobertura(d), 'cobertura-municipal.csv', 'text/csv;charset=utf-8');
    }
  }

  descargarPrioridad(): void {
    this.avisoPrioridad.set(null);
    this.api.prioridadDelVigia().subscribe({
      next: p => {
        if (p.aviso) {
          this.avisoPrioridad.set(p.aviso);
          return;
        }
        this.bajar(textoDePrioridad(p, new Date().toISOString().slice(0, 10)), 'prioridad.txt', 'text/plain;charset=utf-8');
        this.avisoPrioridad.set(`${p.municipios.length} municipios de clientes` +
          (p.sinMunicipio ? `; de ${p.sinMunicipio} clientes no se supo el municipio por su domicilio.` : '.'));
      },
      error: e => this.avisoPrioridad.set(mensajeDeError(e, 'No se pudo generar la lista.')),
    });
  }

  private cargarExpediente(estado: string): void {
    this.errorExpediente.set(null);
    if (this.expedientes()[estado]) {
      return;
    }
    this.api.expediente(estado).subscribe({
      next: x => this.expedientes.update(m => ({ ...m, [estado]: x })),
      error: e => this.errorExpediente.set(mensajeDeError(e, 'No se pudo cargar el expediente del estado.')),
    });
  }

  private bajar(contenido: string, nombre: string, tipo: string): void {
    const url = URL.createObjectURL(new Blob([contenido], { type: tipo }));
    const a = document.createElement('a');
    a.href = url;
    a.download = nombre;
    a.click();
    URL.revokeObjectURL(url);
  }
}
