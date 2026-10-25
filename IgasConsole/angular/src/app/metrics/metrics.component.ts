import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChangeDetectorRef, Component, HostListener, NgZone, OnDestroy, OnInit, inject } from '@angular/core';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { descargarBlob, nombreConFecha } from './descargar';
import { Observable, Subscription, catchError, filter, fromEvent, interval, merge, of, startWith, switchMap } from 'rxjs';
import {
  AuditLogEntryDto,
  AuditLogsService,
  MetricPointDto,
  MetricsSeriesDto,
  MetricsService,
  MonitoringSettingsDto,
  MonitoringSettingsService,
  RateLimitEventDto,
} from './metrics.service';
import { EntityChangeDto, EntityChangesService } from './entity-changes.service';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { CabeceraComponent } from '../shared/cabecera.component';

/** Una línea del gráfico. El color viaja como variable CSS para que el tema claro/oscuro lo resuelva. */
interface Serie {
  nombre: string;
  variableColor: string;
  valores: (number | null)[];
  trazo: string;
}

interface Grafico {
  titulo: string;
  unidad: string;
  series: Serie[];
  maximo: number;
  /** Marcas del eje Y, de abajo arriba. */
  marcasY: { valor: number; y: number }[];
}

/**
 * El SVG escala con su viewBox, asi que TODO lo que hay dentro escala con el: un texto
 * de 11 unidades en un viewBox de 1000 mostrado a 360px de ancho se ve a 4px, ilegible.
 * Por eso hay dos geometrias y no una sola con CSS responsive: en movil el viewBox se
 * acerca al tamano real en pixeles para que las proporciones se mantengan.
 */
const ESCRITORIO = { ancho: 1000, alto: 220, izq: 48, der: 12, sup: 12, inf: 26, marcasX: 5 };
const MOVIL = { ancho: 360, alto: 200, izq: 34, der: 8, sup: 10, inf: 24, marcasX: 3 };

@Component({
  selector: 'app-metrics',
  standalone: true,
  imports: [CommonModule, FormsModule, CoreModule, CabeceraComponent, EsqueletoComponent],
  templateUrl: './metrics.component.html',
  styleUrls: ['./metrics.component.scss'],
})
export class MetricsComponent implements OnInit, OnDestroy {
  private readonly textos = inject(LocalizationService);
  private readonly servicio = inject(MetricsService);
  private readonly servicioAjustes = inject(MonitoringSettingsService);
  private readonly servicioAuditoria = inject(AuditLogsService);
  private readonly servicioCambios = inject(EntityChangesService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  /**
   * Aplicar el cambio y repintar. Ver CLAUDE.md §5.1.
   *
   * ⚠️ Esta pantalla era la ULTIMA sin esta proteccion: es anterior a que se descubriera
   * la trampa y se quedo atras. El sintoma era el descrito alli — la peticion devuelve 200
   * con los datos buenos y la vista se queda congelada en "Cargando metricas...".
   * Con solo la serie envuelta, los graficos aparecian pero las secciones que dependen de
   * OTRA llamada (el registro de peticiones) seguian sin salir: hay que envolver cada
   * callback que cambie algo que se pinte.
   */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }
  private suscripcion?: Subscription;

  readonly rangos = [
    { horas: 1, etiqueta: '1 hora' },
    { horas: 24, etiqueta: '24 horas' },
    { horas: 168, etiqueta: '7 días' },
  ];

  horas = 24;
  cargando = true;
  error?: string;
  datos?: MetricsSeriesDto;
  graficos: Grafico[] = [];
  verTabla = false;

  // --- Trafico bloqueado por rate limiting ---
  bloqueados: RateLimitEventDto[] = [];

  // --- Registro de peticiones ---
  peticiones: AuditLogEntryDto[] = [];
  /** Undefined = todo; 400 = solo lo que fallo. */
  filtroFallos?: number;
  filtroIp = '';
  filtroUsuario = '';
  auditoriaDisponible = false;

  // --- Historial de cambios de entidades --------------------------------------
  // Comparte permiso con el registro de peticiones (IgasConsole.AuditLogs): las dos cosas son
  // el rastro de la actividad de personas, y quien ve una deberia poder ver la otra.
  cambios: EntityChangeDto[] = [];
  totalCambios = 0;
  tiposCambio: string[] = [];
  filtroTipoCambio = '';
  filtroUsuarioCambio = '';
  filtroIdCambio = '';
  filtroDiasCambio = 30;
  paginaCambios = 0;
  readonly cambiosPorPagina = 10;
  cambioAbierto?: string;

  /** Índice sobre el que está el cursor; -1 = fuera. Compartido por los tres gráficos. */
  indiceActivo = -1;

  /** Geometría en uso. La plantilla lee de aquí, no de constantes.
      Se llama 'geo' y no 'g' porque el *ngFor de los gráficos ya usa 'g' y lo sombrearía. */
  geo = ESCRITORIO;

  // --- Configuración de avisos ---
  ajustes?: MonitoringSettingsDto;
  panelAjustesAbierto = false;
  guardandoAjustes = false;
  probando = false;
  mensajeAjustes?: { texto: string; ok: boolean };

  /** El token se escribe, nunca se lee: el backend solo devuelve una pista. */
  tokenNuevo = '';
  chatIdsEditados = '';
  avisosActivos = false;

  ngOnInit(): void {
    this.aplicarGeometria();
    this.cargar();
  }

  @HostListener('window:resize')
  alRedimensionar(): void {
    const antes = this.geo;
    this.aplicarGeometria();

    // Solo se recalculan los trazos si de verdad cambio de geometria: al girar el movil
    // llegan decenas de eventos de resize y rehacer los tres graficos en cada uno es
    // trabajo tirado.
    if (antes !== this.geo && this.datos) {
      this.graficos = this.construirGraficos(this.datos.points);
    }
  }

  private aplicarGeometria(): void {
    this.geo = window.innerWidth < 768 ? MOVIL : ESCRITORIO;
  }

  ngOnDestroy(): void {
    this.suscripcion?.unsubscribe();
  }

  cambiarRango(horas: number): void {
    if (this.horas === horas) {
      return;
    }
    this.horas = horas;
    this.cargar();
  }

  private cargar(): void {
    this.suscripcion?.unsubscribe();
    this.cargando = true;
    this.error = undefined;

    // Se refresca solo cada minuto, que es justo el ritmo al que el servidor toma
    // muestras: hacerlo más a menudo solo repintaría los mismos datos.
    // Se refresca cada minuto Y al volver a la pestana. Mientras esta oculta no se pide
    // nada: en un movil con la pantalla bloqueada esas peticiones mueren a medias y solo
    // sirven para gastar bateria y generar errores fantasma.
    this.suscripcion = merge(interval(60_000), fromEvent(document, 'visibilitychange'))
      .pipe(
        startWith(0),
        filter(() => document.visibilityState === 'visible'),
        switchMap(() => this.pedirSerie()),
        filter((datos): datos is MetricsSeriesDto => datos !== null),
      )
      .subscribe({
        next: datos => this.aplicar(() => {
          // PRIMERO, antes de nada que pueda lanzar. Si se apagara al final, una
          // excepcion construyendo los graficos dejaria el indicador encendido para
          // siempre: RxJS no enruta al handler de error las excepciones lanzadas dentro
          // del propio 'next', las relanza de forma asincrona y aqui no se ven.
          this.cargando = false;
          this.error = undefined;   // un sondeo bueno borra el aviso del anterior
          this.datos = datos;

          try {
            this.graficos = this.construirGraficos(datos?.points ?? []);
          } catch (e) {
            // Un fallo pintando no debe tragarse los datos: la tabla sigue siendo util,
            // y el motivo se ve en vez de quedar en un spinner eterno.
            this.graficos = [];
            this.error = 'No se pudieron dibujar los gráficos: ' + (e as Error)?.message;
            this.verTabla = true;
          }

          // Mismo ritmo que el resto de la pagina: no hace falta un segundo temporizador.
        }),
      });
  }

  exportandoBloqueados = false;
  exportandoPeticiones = false;
  errorExport?: string;

  /**
   * Un fallo se traga AQUI DENTRO, no en el subscribe. Si el error llegara al subscribe,
   * la suscripcion moriria y el refresco automatico no volveria nunca: un corte de red de
   * dos segundos dejaria la pagina congelada hasta recargarla a mano.
   */
  private pedirSerie(): Observable<MetricsSeriesDto | null> {
    return this.servicio.getSeries(this.horas).pipe(
      catchError(e => {
        this.cargando = false;

        // status 0 = la peticion no llego a completarse (conexion perdida, pestana
        // suspendida, peticion abortada). Si ya hay datos en pantalla no se dice nada:
        // el proximo sondeo lo arregla solo y avisar de cada bache seria ruido.
        const esCorteTransitorio = e?.status === 0;
        if (esCorteTransitorio && this.datos) {
          return of(null);
        }

        this.error = esCorteTransitorio
          ? 'Sin conexión con el servidor. Se reintenta solo en un minuto.'
          : 'No se pudieron cargar las métricas: ' + (e?.error?.error?.message ?? e?.message ?? '');

        return of(null);
      }),
    );
  }

  // --- Construcción de los gráficos ---------------------------------------------

  private construirGraficos(puntos: MetricPointDto[]): Grafico[] {
    return [
      this.crear(
        'Uso del sistema',
        '%',
        puntos,
        [
          { nombre: 'CPU', variableColor: '--serie-1', selector: p => p.cpuPercent ?? null },
          { nombre: 'Memoria', variableColor: '--serie-2', selector: p => p.memoryPercent ?? null },
          // Swap en el mismo gráfico porque comparte unidad (%). Se recogía desde el
          // principio y no se mostraba en ninguna parte: dato capturado e invisible.
          { nombre: 'Swap', variableColor: '--serie-3', selector: p => p.swapPercent ?? null },
        ],
        100,
      ),
      // Peticiones y errores comparten unidad (número de peticiones), así que comparten
      // eje. Nunca dos escalas en un mismo gráfico: dos ejes Y mienten sobre la relación
      // entre las series.
      // Las tres series cuentan peticiones, así que comparten eje con propiedad.
      this.crear('Peticiones por intervalo', '', puntos, [
        { nombre: 'Peticiones', variableColor: '--serie-1', selector: p => p.requestCount },
        { nombre: '4xx', variableColor: '--serie-4', selector: p => p.clientErrorCount },
        { nombre: '5xx', variableColor: '--estado-critico', selector: p => p.errorCount },
      ]),
      this.crear('Latencia', 'ms', puntos, [
        { nombre: 'Media', variableColor: '--serie-1', selector: p => p.avgResponseMs ?? null },
        { nombre: 'Máxima', variableColor: '--serie-2', selector: p => p.maxResponseMs ?? null },
      ]),
    ];
  }

  private crear(
    titulo: string,
    unidad: string,
    puntos: MetricPointDto[],
    definiciones: {
      nombre: string;
      variableColor: string;
      selector: (p: MetricPointDto) => number | null;
    }[],
    maximoFijo?: number,
  ): Grafico {
    const series: Serie[] = definiciones.map(d => ({
      nombre: d.nombre,
      variableColor: d.variableColor,
      valores: puntos.map(d.selector),
      trazo: '',
    }));

    let maximo = maximoFijo ?? 0;
    if (maximoFijo === undefined) {
      for (const s of series) {
        for (const v of s.valores) {
          if (v !== null && v > maximo) {
            maximo = v;
          }
        }
      }
      // Un poco de aire arriba, y nunca 0 (dividiríamos entre cero).
      maximo = maximo > 0 ? maximo * 1.15 : 1;
    }

    for (const s of series) {
      s.trazo = this.trazar(s.valores, maximo, puntos.length);
    }

    const marcasY = [0, 0.5, 1].map(f => ({
      valor: Math.round(maximo * f * 100) / 100,
      y: this.escalaY(maximo * f, maximo),
    }));

    return { titulo, unidad, series, maximo, marcasY };
  }

  private escalaX(indice: number, total: number): number {
    if (total <= 1) {
      return this.geo.izq;
    }
    const util = this.geo.ancho - this.geo.izq - this.geo.der;
    return this.geo.izq + (indice / (total - 1)) * util;
  }

  private escalaY(valor: number, maximo: number): number {
    const util = this.geo.alto - this.geo.sup - this.geo.inf;
    return this.geo.sup + util - (valor / maximo) * util;
  }

  /** Los huecos (null) cortan la línea en vez de inventarse una interpolación. */
  private trazar(valores: (number | null)[], maximo: number, total: number): string {
    let d = '';
    let arrancando = true;

    valores.forEach((v, i) => {
      if (v === null) {
        arrancando = true;
        return;
      }
      const x = this.escalaX(i, total).toFixed(1);
      const y = this.escalaY(v, maximo).toFixed(1);
      d += (arrancando ? 'M' : 'L') + x + ',' + y + ' ';
      arrancando = false;
    });

    return d.trim();
  }

  // --- Interacción ---------------------------------------------------------------

  alMover(evento: MouseEvent, svg: Element): void {
    const total = this.datos?.points.length ?? 0;
    if (total === 0) {
      return;
    }

    const caja = svg.getBoundingClientRect();
    const xRelativo = ((evento.clientX - caja.left) / caja.width) * this.geo.ancho;
    const util = this.geo.ancho - this.geo.izq - this.geo.der;
    const fraccion = (xRelativo - this.geo.izq) / util;

    this.indiceActivo = Math.max(0, Math.min(total - 1, Math.round(fraccion * (total - 1))));
  }

  alSalir(): void {
    this.indiceActivo = -1;
  }

  xDe(indice: number): number {
    return this.escalaX(indice, this.datos?.points.length ?? 0);
  }

  yDe(valor: number | null | undefined, maximo: number): number | null {
    return valor === null || valor === undefined ? null : this.escalaY(valor, maximo);
  }

  get puntoActivo(): MetricPointDto | undefined {
    return this.indiceActivo >= 0 ? this.datos?.points[this.indiceActivo] : undefined;
  }

  valorSerie(serie: Serie): number | null {
    return this.indiceActivo >= 0 ? serie.valores[this.indiceActivo] ?? null : null;
  }

  // --- Configuración de avisos ---------------------------------------------------

  // --- Formato -------------------------------------------------------------------

  /**
   * Fecha completa para el historial de cambios.
   * <para>No se reutiliza hora(), que depende de la ventana de tiempo elegida en los
   * graficos: aqui la ventana puede ser de un ano y "26/08 17:52" no distinguiria un
   * cambio de este agosto del del anterior.</para>
   */
  fecha(iso: string): string {
    return new Date(iso).toLocaleString(undefined, {
      dateStyle: 'short',
      timeStyle: 'short',
    });
  }

  hora(iso: string): string {
    const d = new Date(iso);
    return this.horas <= 24
      ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
      : d.toLocaleString([], { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
  }

  /** Etiquetas repartidas en el eje X (menos en móvil, o se solapan). */
  get marcasX(): { indice: number; texto: string }[] {
    const puntos = this.datos?.points ?? [];
    if (puntos.length === 0) {
      return [];
    }
    const cuantas = Math.min(this.geo.marcasX, puntos.length);
    const paso = (puntos.length - 1) / Math.max(1, cuantas - 1);
    return Array.from({ length: cuantas }, (_, i) => {
      const indice = Math.round(i * paso);
      return { indice, texto: this.hora(puntos[indice].bucket) };
    });
  }

  numero(valor: number | null | undefined, decimales = 1): string {
    return valor === null || valor === undefined ? '—' : valor.toFixed(decimales);
  }

  /**
   * Si el recolector muere, los gráficos se quedan quietos con datos viejos y la página
   * parece perfectamente sana. Ese es el peor fallo posible en un panel de monitoreo:
   * engaña en silencio. Se tolera hasta el triple del intervalo antes de avisar, para no
   * saltar por un ciclo perdido.
   */
  get datosCaducados(): boolean {
    const segundos = this.datos?.secondsSinceLastSample;
    const intervalo = this.datos?.sampleIntervalSeconds ?? 60;
    return segundos != null && segundos > intervalo * 3;
  }

  get antiguedadTexto(): string {
    const s = this.datos?.secondsSinceLastSample;
    if (s == null) {
      return '';
    }
    if (s < 90) {
      return `hace ${Math.round(s)} s`;
    }
    if (s < 5400) {
      return `hace ${Math.round(s / 60)} min`;
    }
    return `hace ${Math.round(s / 3600)} h`;
  }

  get reiniciosEnRango(): number {
    return (this.datos?.points ?? []).reduce((t, p) => t + (p.restarts ?? 0), 0);
  }

  get sinDatos(): boolean {
    return !this.cargando && !this.error && (this.datos?.points.length ?? 0) === 0;
  }
  // --- Historial de cambios de entidades --------------------------------------

}
