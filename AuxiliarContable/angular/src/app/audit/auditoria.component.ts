import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CoreModule, LocalizationService } from '@abp/ng.core';
import { ActivatedRoute } from '@angular/router';
import { CabeceraComponent } from '../shared/cabecera.component';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { AuditLogEntryDto, AuditLogsService } from '../metrics/metrics.service';
import { EntityChangeDto, EntityChangesService } from '../metrics/entity-changes.service';
import { descargarBlob, nombreConFecha } from '../metrics/descargar';

/**
 * El rastro de las personas: qué se pidió y qué cambió.
 *
 * <p>⚠️ <b>Estaba dentro de `/metrics`, y ahí no pintaba nada.</b> Aquella pantalla medía
 * <b>11.297 px — 12,6 pantallas de scroll</b>, con 3 tablas, 37 controles y 11 llamadas a la
 * API al abrirla, porque juntaba cuatro cosas que no se parecen: gráficos del servidor,
 * tráfico bloqueado, auditoría y la configuración del bot de Telegram. Medido, no opinado.</p>
 *
 * <p>Las dos secciones que viven <b>aquí</b> sí van juntas y por una razón concreta: el
 * registro de peticiones dice <b>qué endpoint</b> se llamó y el historial de cambios dice
 * <b>qué campo</b> se modificó y de qué valor a cuál. Son la misma pregunta —«¿quién tocó
 * esto?»— contestada a dos niveles, y comparten el mismo permiso
 * (<c>AuxiliarContable.AuditLogs</c>).</p>
 */
@Component({
  selector: 'app-auditoria',
  standalone: true,
  imports: [CommonModule, FormsModule, CoreModule, CabeceraComponent, EsqueletoComponent],
  templateUrl: './auditoria.component.html',
  styleUrls: ['./auditoria.component.scss'],
})
export class AuditoriaComponent implements OnInit {
  private readonly textos = inject(LocalizationService);
  private readonly servicioAuditoria = inject(AuditLogsService);
  private readonly servicioCambios = inject(EntityChangesService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  cargando = true;
  error?: string;

  /**
   * ⚠️ Todo cambio de estado va dentro de `NgZone.run` + `detectChanges`, y hay que envolver
   * CADA callback, no solo el principal: §5.1 documenta que con el arreglo a medias los
   * gráficos aparecían y las secciones que dependían de otra llamada no.
   */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  // --- Registro de peticiones -------------------------------------------------
  peticiones: AuditLogEntryDto[] = [];
  horas = 24;
  filtroFallos?: number;
  filtroIp = '';
  filtroUsuario = '';

  /**
   * Sin el permiso `AuxiliarContable.AuditLogs` el backend da 403 y la sección no se muestra. Se
   * decide con la respuesta y no con el permiso del cliente: quien manda es el servidor.
   */
  auditoriaDisponible = false;

  // --- Historial de cambios de entidades --------------------------------------
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

  // ⚠️ El registro tambien se pagina, por lo mismo que las sesiones: el servidor devuelve
  // hasta 200 entradas y pintarlas todas daba 9.764 px de scroll. El historial de cambios
  // que hay debajo ya paginaba; esta tabla no, y era la que ocupaba casi todo.
  //
  // En el CLIENTE: las 200 ya vienen en la respuesta y el filtrado de verdad —horas, IP,
  // usuario, solo fallos— lo hace el servidor. Paginar tambien alli seria una segunda
  // llamada por pagina para recortar algo que ya se tiene.
  paginaPeticiones = 0;
  readonly peticionesPorPagina = 15;

  get paginaDePeticiones(): AuditLogEntryDto[] {
    const i = this.paginaPeticiones * this.peticionesPorPagina;
    return this.peticiones.slice(i, i + this.peticionesPorPagina);
  }

  get hayMasPeticiones(): boolean {
    return (this.paginaPeticiones + 1) * this.peticionesPorPagina < this.peticiones.length;
  }

  paginaPeticionesAnterior(): void {
    if (this.paginaPeticiones > 0) { this.paginaPeticiones--; }
  }

  paginaPeticionesSiguiente(): void {
    if (this.hayMasPeticiones) { this.paginaPeticiones++; }
  }

  exportandoPeticiones = false;
  errorExport?: string;

  readonly rangos = [
    { horas: 1, etiqueta: '1 h' },
    { horas: 24, etiqueta: '24 h' },
    { horas: 168, etiqueta: '7 d' },
  ];

  ngOnInit(): void {
    // Se llega aquí desde la tabla de tráfico bloqueado de /security con ?ip=…, que es la
    // pregunta siguiente a «esta IP fue bloqueada»: qué más hizo. Antes las dos tablas
    // estaban en la misma pantalla y el atajo filtraba en sitio; ahora navega.
    const ip = this.ruta.snapshot.queryParamMap.get('ip');
    if (ip) {
      this.filtroIp = ip;
    }

    this.cargarPeticiones();
    this.cargarCambios();
  }

  cambiarRango(horas: number): void {
    this.horas = horas;
    this.cargarPeticiones();
  }

  cargarPeticiones(): void {
    this.servicioAuditoria
      .getRecent(this.horas, this.filtroFallos, this.filtroIp, this.filtroUsuario)
      .subscribe({
        next: entradas =>
          this.aplicar(() => {
            this.peticiones = entradas;
            // Vuelve a la primera pagina: quedarse en la 5 de un filtro nuevo enseña una
            // lista vacia que parece «no hay resultados».
            this.paginaPeticiones = 0;
            this.auditoriaDisponible = true;
            this.cargando = false;
          }),
        error: () =>
          this.aplicar(() => {
            this.auditoriaDisponible = false;
            this.cargando = false;
          }),
      });
  }

  cambiarFiltroFallos(soloFallos: boolean): void {
    this.filtroFallos = soloFallos ? 400 : undefined;
    this.cargarPeticiones();
  }

  limpiarFiltros(): void {
    this.filtroIp = '';
    this.filtroUsuario = '';
    this.cargarPeticiones();
  }

  get hayFiltros(): boolean {
    return this.filtroIp.trim() !== '' || this.filtroUsuario.trim() !== '';
  }

  /**
   * Hora corta cuando la ventana es de un dia o menos; con dia y mes cuando es mayor.
   * <p>En una ventana de siete dias, «17:52» no distingue el martes del jueves.</p>
   */
  hora(iso: string): string {
    const d = new Date(iso);
    return this.horas <= 24
      ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
      : d.toLocaleString([], { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
  }

  /** Descarga el registro con LOS MISMOS filtros que la tabla. */
  exportarPeticiones(): void {
    this.exportandoPeticiones = true;
    this.errorExport = undefined;

    this.servicioAuditoria
      .exportRecent(this.horas, this.filtroFallos, this.filtroIp, this.filtroUsuario)
      .subscribe({
        next: blob =>
          this.aplicar(() => {
            descargarBlob(blob, nombreConFecha('registro-peticiones'));
            this.exportandoPeticiones = false;
          }),
        error: () =>
          this.aplicar(() => {
            this.exportandoPeticiones = false;
            this.errorExport = this.textos.instant('::Common:DownloadError');
          }),
      });
  }

  cargarCambios(): void {
    // Los tipos del desplegable salen de los datos, no de una lista fija: los nombres los
    // pone ABP y pueden cambiar al actualizar el módulo.
    if (this.tiposCambio.length === 0) {
      this.servicioCambios.getFilters().subscribe({
        next: f => this.aplicar(() => (this.tiposCambio = f.entityTypes)),
        error: () => this.aplicar(() => (this.tiposCambio = [])),
      });
    }

    this.servicioCambios
      .getList({
        days: this.filtroDiasCambio,
        entityTypeFullName: this.filtroTipoCambio || undefined,
        entityId: this.filtroIdCambio || undefined,
        userName: this.filtroUsuarioCambio || undefined,
        skipCount: this.paginaCambios * this.cambiosPorPagina,
        maxResultCount: this.cambiosPorPagina,
      })
      .subscribe({
        next: r =>
          this.aplicar(() => {
            this.cambios = r.items;
            this.totalCambios = r.totalCount;
          }),
        error: () =>
          this.aplicar(() => {
            this.cambios = [];
            this.totalCambios = 0;
          }),
      });
  }

  filtrarCambios(): void {
    // Cualquier cambio de filtro vuelve a la primera página: quedarse en la página 3 de un
    // filtro nuevo enseña una lista vacía que parece «no hay resultados».
    this.paginaCambios = 0;
    this.cambioAbierto = undefined;
    this.cargarCambios();
  }

  paginaCambiosAnterior(): void {
    if (this.paginaCambios > 0) {
      this.paginaCambios--;
      this.cargarCambios();
    }
  }

  paginaCambiosSiguiente(): void {
    if (this.hayMasCambios) {
      this.paginaCambios++;
      this.cargarCambios();
    }
  }

  get hayMasCambios(): boolean {
    return (this.paginaCambios + 1) * this.cambiosPorPagina < this.totalCambios;
  }

  alternarCambio(c: EntityChangeDto): void {
    this.cambioAbierto = this.cambioAbierto === c.id ? undefined : c.id;
  }

  /** Verde crear, ámbar modificar, rojo borrar: se lee la fila sin leer la palabra. */
  claseTipoCambio(tipo: string): string {
    switch (tipo) {
      case 'Created':
        return 'text-bg-success';
      case 'Deleted':
        return 'text-bg-danger';
      default:
        return 'text-bg-warning';
    }
  }

  textoTipoCambio(tipo: string): string {
    switch (tipo) {
      case 'Created':
        return this.textos.instant('::EntityChanges:Created');
      case 'Deleted':
        return this.textos.instant('::EntityChanges:Deleted');
      default:
        return this.textos.instant('::EntityChanges:Updated');
    }
  }

  /** Un valor ausente se enseña como raya, no como cadena vacía: se distingue de «se borró el texto». */
  valor(v?: string | null): string {
    return v === null || v === undefined || v === '' ? '—' : v;
  }

  fecha(iso?: string | null): string {
    return iso ? new Date(iso).toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' }) : '—';
  }
}
