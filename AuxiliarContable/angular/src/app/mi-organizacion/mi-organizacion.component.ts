import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfigStateService } from '@abp/ng.core';
import { Confirmation, ConfirmationService, ToasterService } from '@abp/ng.theme.shared';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { PAPELES, RolEnOrganizacion, textoDePapel } from '../shared/papeles';
import { MiembroDeMiOrganizacionDto, MiOrganizacionDto, MiOrganizacionService } from './mi-organizacion.service';

/**
 * «Mi organización»: el CLIENTE que administra su organización gestiona aquí a su gente
 * (invitar, cambiar su papel, quitarla). Quien solo es miembro ve su organización y su papel.
 * Las reglas viven en el servidor (Autorizador + PermisosEnOrganizacion); aquí solo se pinta.
 */
@Component({
  selector: 'app-mi-organizacion',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './mi-organizacion.component.html',
  styleUrls: ['./mi-organizacion.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MiOrganizacionComponent implements OnInit {
  private readonly api = inject(MiOrganizacionService);
  private readonly avisos = inject(ToasterService);
  private readonly confirmacion = inject(ConfirmationService);
  private readonly config = inject(ConfigStateService);

  readonly papeles = PAPELES;
  readonly textoDePapel = textoDePapel;

  readonly organizaciones = signal<MiOrganizacionDto[] | null>(null);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal<string | null>(null);
  readonly yo = this.config.getOne('currentUser')?.id as string | undefined;

  /** El formulario de invitar, por organización. */
  correo: Record<string, string> = {};
  papelNuevo: Record<string, RolEnOrganizacion> = {};
  readonly errorDeInvitacion = signal<Record<string, string | null>>({});

  ngOnInit(): void {
    this.api.getList().subscribe({
      next: o => {
        for (const x of o) {
          this.papelNuevo[x.id] ??= RolEnOrganizacion.Miembro;
        }
        this.organizaciones.set(o);
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar tu organización.')),
    });
  }

  invitar(o: MiOrganizacionDto): void {
    const email = (this.correo[o.id] ?? '').trim();
    if (!email) {
      return;
    }
    this.ocupado.set('invitar-' + o.id);
    this.errorDeInvitacion.set({ ...this.errorDeInvitacion(), [o.id]: null });
    this.api.invitar(o.id, email, this.papelNuevo[o.id] ?? RolEnOrganizacion.Miembro).subscribe({
      next: r => {
        this.ocupado.set(null);
        this.correo[o.id] = '';
        this.reemplazar(r);
        this.avisos.success(`Le enviamos la invitación a ${email}.`);
      },
      error: e => {
        this.ocupado.set(null);
        this.errorDeInvitacion.set({ ...this.errorDeInvitacion(), [o.id]: mensajeDeError(e, 'No se pudo invitar.') });
      },
    });
  }

  cambiarPapel(o: MiOrganizacionDto, m: MiembroDeMiOrganizacionDto, rol: RolEnOrganizacion): void {
    this.ocupado.set(m.userId);
    this.api.cambiarRol(o.id, m.userId, rol).subscribe({
      next: r => {
        this.ocupado.set(null);
        this.reemplazar(r);
      },
      error: e => {
        this.ocupado.set(null);
        this.avisos.error(mensajeDeError(e, 'No se pudo cambiar su papel.'));
        this.reemplazar({ ...o });
      },
    });
  }

  quitar(o: MiOrganizacionDto, m: MiembroDeMiOrganizacionDto): void {
    const nombre = m.nombreCompleto ?? m.userName;
    this.confirmacion
      .warn(`${nombre} dejará de ver los análisis de ${o.nombre}. Su cuenta no se borra.`, '¿Quitar de la organización?', {
        yesText: 'Quitar',
        cancelText: 'Cancelar',
      })
      .subscribe(estado => {
        if (estado !== Confirmation.Status.confirm) {
          return;
        }
        this.ocupado.set(m.userId);
        this.api.quitar(o.id, m.userId).subscribe({
          next: r => {
            this.ocupado.set(null);
            this.reemplazar(r);
          },
          error: e => {
            this.ocupado.set(null);
            this.avisos.error(mensajeDeError(e, 'No se pudo quitar.'));
          },
        });
      });
  }

  revocar(o: MiOrganizacionDto, invitacionId: string): void {
    this.ocupado.set(invitacionId);
    this.api.revocarInvitacion(o.id, invitacionId).subscribe({
      next: r => {
        this.ocupado.set(null);
        this.reemplazar(r);
      },
      error: e => {
        this.ocupado.set(null);
        this.avisos.error(mensajeDeError(e, 'No se pudo cancelar la invitación.'));
      },
    });
  }

  iniciales(m: MiembroDeMiOrganizacionDto): string {
    return (m.nombreCompleto ?? m.userName)
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map(p => p[0]!.toUpperCase())
      .join('');
  }

  detalleDe(r: RolEnOrganizacion): string {
    return PAPELES.find(p => p.valor === r)?.detalle ?? '';
  }

  fecha(iso: string): string {
    return new Date(iso).toLocaleDateString('es-MX', { day: 'numeric', month: 'short' });
  }

  private reemplazar(r: MiOrganizacionDto): void {
    this.organizaciones.set((this.organizaciones() ?? []).map(o => (o.id === r.id ? r : o)));
  }
}
