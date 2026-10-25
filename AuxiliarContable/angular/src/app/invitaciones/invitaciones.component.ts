import { PAPELES, RolEnOrganizacion } from '../shared/papeles';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Confirmation, ConfirmationService, ToasterService } from '@abp/ng.theme.shared';
import {
  EstadoInvitacion,
  InvitacionDto,
  InvitacionesService,
  OpcionesDeInvitacionDto,
  RolInvitableDto,
  mensajeDeError,
} from './invitaciones.service';
import { describirRol } from '../shared/roles';
import {
  MembresiasService,
  OpcionesDeOrganizacionDto,
  OrganizacionResumenDto,
  necesitaOrganizacion,
} from '../shared/membresias.service';
import { SelectorOrganizacionComponent } from '../shared/selector-organizacion.component';

interface Filtro {
  etiqueta: string;
  estado: EstadoInvitacion | null;
}

@Component({
  selector: 'app-invitaciones',
  standalone: true,
  imports: [FormsModule, DatePipe, SelectorOrganizacionComponent],
  templateUrl: './invitaciones.component.html',
  styleUrls: ['./invitaciones.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InvitacionesComponent implements OnInit {
  private readonly api = inject(InvitacionesService);
  private readonly avisos = inject(ToasterService);
  private readonly confirmacion = inject(ConfirmationService);
  private readonly membresias = inject(MembresiasService);

  readonly Estado = EstadoInvitacion;
  readonly filtros: Filtro[] = [
    { etiqueta: 'Todas', estado: null },
    { etiqueta: 'Pendientes', estado: EstadoInvitacion.Pendiente },
    { etiqueta: 'Aceptadas', estado: EstadoInvitacion.Aceptada },
    { etiqueta: 'Vencidas', estado: EstadoInvitacion.Vencida },
    { etiqueta: 'Revocadas', estado: EstadoInvitacion.Revocada },
  ];

  readonly opciones = signal<OpcionesDeInvitacionDto | null>(null);
  readonly lista = signal<InvitacionDto[]>([]);
  readonly total = signal(0);
  readonly cargando = signal(true);
  readonly enviando = signal(false);
  readonly ocupada = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  readonly filtro = signal<EstadoInvitacion | null>(null);
  busqueda = '';
  email = '';
  readonly rol = signal<string | null>(null);
  readonly organizacionId = signal<string | null>(null);
  readonly papeles = PAPELES;
  /** Su papel dentro de la organización (solo para clientes). */
  readonly papel = signal<RolEnOrganizacion>(RolEnOrganizacion.Miembro);
  readonly detalleDelPapel = computed(() => PAPELES.find(p => p.valor === this.papel())?.detalle ?? '');
  readonly opcionesOrg = signal<OpcionesDeOrganizacionDto | null>(null);
  readonly organizaciones = signal<OrganizacionResumenDto[]>([]);

  readonly roles = computed(() => this.opciones()?.roles ?? []);
  readonly correoListo = computed(() => this.opciones()?.correoConfigurado ?? true);

  /** El rol elegido es de cliente: entra dentro de una organización (lo exige el backend). */
  readonly pideOrganizacion = computed(() => {
    const elegido = this.roles().find(r => r.nombre === this.rol());
    return !!elegido && necesitaOrganizacion(elegido.nivel, this.opcionesOrg());
  });

  readonly listo = computed(
    () => !!this.rol() && this.correoListo() && (!this.pideOrganizacion() || !!this.organizacionId()),
  );

  ngOnInit(): void {
    this.api.getOpciones().subscribe({
      next: o => {
        this.opciones.set(o);
        // El más bajo por defecto: es la invitación más común y la menos arriesgada.
        this.rol.set(o.roles.at(-1)?.nombre ?? null);
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudieron cargar los roles.')),
    });
    this.cargar();
    this.membresias.getOpciones().subscribe({
      next: o => {
        this.opcionesOrg.set(o);
        this.organizaciones.set(o.organizaciones);
      },
    });
  }

  anadirOrganizacion(o: OrganizacionResumenDto): void {
    this.organizaciones.set([...this.organizaciones(), o].sort((a, b) => a.nombre.localeCompare(b.nombre)));
  }

  descripcion(rol: RolInvitableDto) {
    return describirRol(rol.nombre);
  }

  cargar(): void {
    this.cargando.set(true);
    this.api
      .getList({ filtro: this.busqueda.trim(), estado: this.filtro(), skipCount: 0, maxResultCount: 100 })
      .subscribe({
        next: r => {
          this.lista.set(r.items ?? []);
          this.total.set(r.totalCount ?? 0);
          this.cargando.set(false);
        },
        error: e => {
          this.error.set(mensajeDeError(e, 'No se pudo cargar la lista.'));
          this.cargando.set(false);
        },
      });
  }

  cambiarFiltro(estado: EstadoInvitacion | null): void {
    this.filtro.set(estado);
    this.cargar();
  }

  invitar(): void {
    const email = this.email.trim();
    const rol = this.rol();
    if (!email || !rol || this.enviando() || !this.listo()) {
      return;
    }

    this.enviando.set(true);
    this.api.crear(email, rol, this.pideOrganizacion() ? this.organizacionId() : null, this.papel()).subscribe({
      next: inv => {
        this.enviando.set(false);
        this.email = '';
        this.avisos.success(`Invitación en camino a ${inv.email}. El enlace vale ${this.opciones()?.diasDeVigencia ?? 7} días.`, 'Invitación enviada');
        this.cargar();
      },
      error: e => {
        this.enviando.set(false);
        this.avisos.error(mensajeDeError(e), 'No se envió');
      },
    });
  }

  reenviar(inv: InvitacionDto): void {
    this.ocupada.set(inv.id);
    this.api.reenviar(inv.id).subscribe({
      next: () => {
        this.ocupada.set(null);
        this.avisos.success(`Se mandó un enlace nuevo a ${inv.email}. El anterior ya no sirve.`, 'Reenviada');
        this.cargar();
      },
      error: e => {
        this.ocupada.set(null);
        this.avisos.error(mensajeDeError(e), 'No se reenvió');
      },
    });
  }

  revocar(inv: InvitacionDto): void {
    this.confirmacion
      .warn(`El enlace que recibió ${inv.email} dejará de funcionar.`, '¿Revocar la invitación?', {
        yesText: 'Revocar',
        cancelText: 'Cancelar',
      })
      .subscribe(estado => {
        if (estado !== Confirmation.Status.confirm) {
          return;
        }
        this.ocupada.set(inv.id);
        this.api.revocar(inv.id).subscribe({
          next: () => {
            this.ocupada.set(null);
            this.cargar();
          },
          error: e => {
            this.ocupada.set(null);
            this.avisos.error(mensajeDeError(e), 'No se revocó');
          },
        });
      });
  }

  etiquetaEstado(inv: InvitacionDto): { texto: string; clase: string } {
    switch (inv.estado) {
      case EstadoInvitacion.Aceptada:
        return { texto: 'Aceptada', clase: 'ig-etiqueta-exito' };
      case EstadoInvitacion.Revocada:
        return { texto: 'Revocada', clase: 'ig-etiqueta-error' };
      case EstadoInvitacion.Vencida:
        return { texto: 'Vencida', clase: 'ig-etiqueta-alerta' };
      default:
        return inv.enCola
          ? { texto: 'Enviando…', clase: 'ig-etiqueta-tinta' }
          : { texto: 'Pendiente', clase: 'ig-etiqueta-acento' };
    }
  }

  puedeReenviar(inv: InvitacionDto): boolean {
    return inv.estado === EstadoInvitacion.Pendiente || inv.estado === EstadoInvitacion.Vencida;
  }

  puedeRevocar(inv: InvitacionDto): boolean {
    return inv.estado === EstadoInvitacion.Pendiente || inv.estado === EstadoInvitacion.Vencida;
  }
}
