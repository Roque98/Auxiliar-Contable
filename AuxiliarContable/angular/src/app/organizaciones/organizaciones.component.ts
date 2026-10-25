import { PAPELES, RolEnOrganizacion } from '../shared/papeles';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Confirmation, ConfirmationService, ToasterService } from '@abp/ng.theme.shared';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import {
  MiembroDto,
  OrganizacionDetalleDto,
  OrganizacionDto,
  OrganizacionesService,
} from './organizaciones.service';

interface Borrador {
  nombre: string;
  descripcion: string;
}

/**
 * Organizaciones: los clientes del despacho y sus personas; pertenecer a una es lo que deja
 * a la gente de un cliente ver y subir su información. El equipo ve todo sin pertenecer a nada.
 */
@Component({
  selector: 'app-organizaciones',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './organizaciones.component.html',
  styleUrls: ['./organizaciones.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrganizacionesComponent implements OnInit {
  private readonly api = inject(OrganizacionesService);
  private readonly avisos = inject(ToasterService);
  private readonly confirmacion = inject(ConfirmationService);

  readonly lista = signal<OrganizacionDto[]>([]);
  readonly cargando = signal(true);
  readonly error = signal<string | null>(null);
  filtro = '';

  /** null = nada abierto; 'nueva' = creando; si no, la abierta. */
  readonly abierta = signal<OrganizacionDetalleDto | 'nueva' | null>(null);
  readonly borrador = signal<Borrador>({ nombre: '', descripcion: '' });
  readonly guardando = signal(false);
  readonly errorDeEdicion = signal<string | null>(null);

  readonly candidatos = signal<MiembroDto[]>([]);
  readonly buscandoCandidatos = signal(false);
  filtroCandidatos = '';
  readonly ocupado = signal<string | null>(null);

  readonly detalle = computed(() => {
    const a = this.abierta();
    return a && a !== 'nueva' ? a : null;
  });

  ngOnInit(): void {
    this.cargar();
  }

  cargar(): void {
    this.cargando.set(true);
    this.api.getList(this.filtro).subscribe({
      next: r => {
        this.lista.set(r.items ?? []);
        this.cargando.set(false);
        this.error.set(null);
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudieron cargar las organizaciones.'));
        this.cargando.set(false);
      },
    });
  }

  nueva(): void {
    this.abierta.set('nueva');
    this.borrador.set({ nombre: '', descripcion: '' });
    this.errorDeEdicion.set(null);
    this.candidatos.set([]);
  }

  abrir(o: OrganizacionDto): void {
    this.api.get(o.id).subscribe({
      next: d => this.mostrar(d),
      error: e => this.avisos.error(mensajeDeError(e, 'No se pudo abrir la organización.')),
    });
  }

  cerrar(): void {
    this.abierta.set(null);
  }

  private mostrar(d: OrganizacionDetalleDto): void {
    this.abierta.set(d);
    this.borrador.set({
      nombre: d.nombre,
      descripcion: d.descripcion ?? '',
    });
    this.errorDeEdicion.set(null);
    this.filtroCandidatos = '';
    this.buscarCandidatos();
  }

  cambiar(campo: 'nombre' | 'descripcion', valor: string): void {
    this.borrador.set({ ...this.borrador(), [campo]: valor });
  }

  // --- Guardar / borrar ---------------------------------------------------------

  guardar(): void {
    const b = this.borrador();
    if (!b.nombre.trim()) {
      this.errorDeEdicion.set('Ponle un nombre a la organización.');
      return;
    }
    const cuerpo = {
      nombre: b.nombre.trim(),
      descripcion: b.descripcion.trim() || null,
    };
    const d = this.detalle();
    this.guardando.set(true);
    this.errorDeEdicion.set(null);
    (d ? this.api.update(d.id, cuerpo) : this.api.create(cuerpo)).subscribe({
      next: r => {
        this.guardando.set(false);
        this.avisos.success(d ? 'Cambios guardados.' : 'Organización creada. Ahora añade a las personas del cliente.');
        this.mostrar(r);
        this.cargar();
      },
      error: e => {
        this.guardando.set(false);
        this.errorDeEdicion.set(mensajeDeError(e, 'No se pudo guardar.'));
      },
    });
  }

  eliminar(): void {
    const d = this.detalle();
    if (!d) {
      return;
    }
    this.confirmacion
      .warn(
        `Sus miembros dejarán de ver la información de esta organización.`,
        `¿Eliminar «${d.nombre}»?`,
        { yesText: 'Eliminar', cancelText: 'Cancelar' },
      )
      .subscribe(estado => {
        if (estado !== Confirmation.Status.confirm) {
          return;
        }
        this.api.delete(d.id).subscribe({
          next: () => {
            this.avisos.success('Organización eliminada.');
            this.cerrar();
            this.cargar();
          },
          error: e => this.avisos.error(mensajeDeError(e, 'No se pudo eliminar.')),
        });
      });
  }

  // --- Miembros -----------------------------------------------------------------

  buscarCandidatos(): void {
    const d = this.detalle();
    if (!d) {
      return;
    }
    this.buscandoCandidatos.set(true);
    this.api.candidatos(d.id, this.filtroCandidatos).subscribe({
      next: c => {
        this.candidatos.set(c);
        this.buscandoCandidatos.set(false);
      },
      error: () => this.buscandoCandidatos.set(false),
    });
  }

  agregar(m: MiembroDto): void {
    const d = this.detalle();
    if (!d) {
      return;
    }
    this.ocupado.set(m.userId);
    this.api.agregarMiembro(d.id, m.userId).subscribe({
      next: r => {
        this.ocupado.set(null);
        this.actualizarMiembros(r);
      },
      error: e => {
        this.ocupado.set(null);
        this.avisos.error(mensajeDeError(e, 'No se pudo añadir.'));
      },
    });
  }

  readonly papeles = PAPELES;

  cambiarPapel(m: MiembroDto, rol: RolEnOrganizacion): void {
    const d = this.detalle();
    if (!d) {
      return;
    }
    this.ocupado.set(m.userId);
    this.api.cambiarRolDeMiembro(d.id, m.userId, rol).subscribe({
      next: r => {
        this.ocupado.set(null);
        this.actualizarMiembros(r);
      },
      error: e => {
        this.ocupado.set(null);
        this.avisos.error(mensajeDeError(e, 'No se pudo cambiar su papel.'));
        // Vuelve a pintar el papel que tenía.
        this.abierta.set({ ...d });
      },
    });
  }

  quitar(m: MiembroDto): void {
    const d = this.detalle();
    if (!d) {
      return;
    }
    this.ocupado.set(m.userId);
    this.api.quitarMiembro(d.id, m.userId).subscribe({
      next: r => {
        this.ocupado.set(null);
        this.actualizarMiembros(r);
      },
      error: e => {
        this.ocupado.set(null);
        this.avisos.error(mensajeDeError(e, 'No se pudo quitar.'));
      },
    });
  }

  /** Solo los miembros: lo que haya a medio editar en el formulario no se pierde. */
  private actualizarMiembros(r: OrganizacionDetalleDto): void {
    this.abierta.set(r);
    this.lista.set(this.lista().map(o => (o.id === r.id ? { ...o, miembros: r.miembros } : o)));
    this.buscarCandidatos();
  }

  iniciales(m: MiembroDto): string {
    const base = m.nombreCompleto || m.userName;
    return base
      .split(/\s+/)
      .slice(0, 2)
      .map(p => p.charAt(0).toUpperCase())
      .join('');
  }
}
