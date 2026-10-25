import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfigStateService, PermissionService } from '@abp/ng.core';
import { IdentityRoleDto, IdentityUserDto, IdentityUserService } from '@abp/ng.identity/proxy';
import { PermissionManagementComponent } from '@abp/ng.permission-management';
import {
  Confirmation,
  ConfirmationService,
  ModalCloseDirective,
  ModalComponent,
  ToasterService,
} from '@abp/ng.theme.shared';
import { Subject, catchError, debounceTime, forkJoin, map, of, switchMap } from 'rxjs';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { describirRol, etiquetaRol, nivelRol, ordenarRoles } from '../shared/roles';
import { SeguridadDeUsuarioDto, SeguridadDeUsuariosService } from '../seguridad/seguridad.service';
import {
  MembresiasService,
  OpcionesDeOrganizacionDto,
  OrganizacionResumenDto,
  necesitaOrganizacion,
} from '../shared/membresias.service';
import { SelectorOrganizacionComponent } from '../shared/selector-organizacion.component';

const POR_PAGINA = 20;

/**
 * Correo provisional de quien se da de alta sin correo (ABP exige uno y único). Mismo valor
 * que SeguridadConsts.DominioProvisional en el backend; `.invalid` no existe (RFC 2606) y el
 * emisor de correo lo descarta. La persona pone el suyo desde «Seguridad de tu cuenta».
 */
const DOMINIO_PROVISIONAL = 'sin-correo.invalid';

export function esCorreoProvisional(correo: string | null | undefined): boolean {
  return !!correo && correo.toLowerCase().endsWith('@' + DOMINIO_PROVISIONAL);
}

interface Fila {
  usuario: IdentityUserDto;
  roles: string[];
  organizaciones: OrganizacionResumenDto[];
}

interface Formulario {
  id: string | null;
  concurrencyStamp?: string;
  userName: string;
  name: string;
  surname: string;
  email: string;
  /** Alta sin correo: se guarda uno provisional y la persona registra el suyo al entrar. */
  sinCorreo: boolean;
  phoneNumber: string;
  password: string;
  isActive: boolean;
  lockoutEnabled: boolean;
  /**
   * UN solo rol: la jerarquía ya hace que cada rol incluya lo del de abajo, así que marcar
   * varios no añade nada y confunde. Null si no tiene ninguno que uno pueda asignar.
   */
  rol: string | null;
  /** Los roles que ya tenía (para no ocultar los que quien edita no alcanza). */
  rolesActuales: string[];
  /** Organización en la que entra (alta) o a la que se añade (edición). */
  organizacionId: string | null;
  /** Las que ya tiene, al editar. */
  organizacionesActuales: OrganizacionResumenDto[];
}

/**
 * La pantalla de Usuarios, en lugar de la de `@abp/ng.identity` (se registra con la clave
 * `Identity.UsersComponent`; la ruta y el menú siguen siendo los de ABP).
 *
 * <p>Por qué propia: la de ABP es una ngx-datatable que calcula el ancho de cada columna en
 * código. En el teléfono había que desplazarse de lado, las columnas se cortaban y las
 * acciones vivían en un desplegable. Aquí cada persona es una ficha: fila en escritorio,
 * tarjeta en el teléfono, con las acciones a la vista.</p>
 *
 * <p>La API es la misma de ABP (`/api/identity/users`), que en el backend pasa por la
 * jerarquía (IgasIdentityUserAppService): lo que no se alcanza ni siquiera llega aquí.</p>
 */
@Component({
  selector: 'app-usuarios',
  standalone: true,
  imports: [
    FormsModule,
    DatePipe,
    RouterLink,
    ModalComponent,
    ModalCloseDirective,
    PermissionManagementComponent,
    SelectorOrganizacionComponent,
  ],
  templateUrl: './usuarios.component.html',
  styleUrls: ['./usuarios.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsuariosComponent implements OnInit {
  private readonly api = inject(IdentityUserService);
  private readonly permisos = inject(PermissionService);
  private readonly avisos = inject(ToasterService);
  private readonly confirmacion = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly seguridadApi = inject(SeguridadDeUsuariosService);
  private readonly membresias = inject(MembresiasService);
  private readonly sesion = toSignal(inject(ConfigStateService).getOne$('currentUser'), { initialValue: null });

  readonly etiquetaRol = etiquetaRol;
  readonly esCorreoProvisional = esCorreoProvisional;

  readonly filas = signal<Fila[]>([]);
  readonly total = signal(0);
  readonly pagina = signal(0);
  readonly cargando = signal(true);
  readonly error = signal<string | null>(null);
  readonly ocupado = signal<string | null>(null);
  busqueda = '';
  private readonly buscar$ = new Subject<string>();

  readonly puedeCrear = this.permisos.getGrantedPolicy('AbpIdentity.Users.Create');
  readonly puedeEditar = this.permisos.getGrantedPolicy('AbpIdentity.Users.Update');
  readonly puedeBorrar = this.permisos.getGrantedPolicy('AbpIdentity.Users.Delete');
  readonly puedeRoles = this.permisos.getGrantedPolicy('AbpIdentity.Users.Update.ManageRoles');
  readonly puedePermisos = this.permisos.getGrantedPolicy('AbpIdentity.Users.ManagePermissions');

  readonly paginas = computed(() => Math.max(1, Math.ceil(this.total() / POR_PAGINA)));
  readonly idActual = computed(() => this.sesion()?.id ?? null);

  // --- Diálogo de alta/edición ---
  readonly dialogo = signal(false);
  readonly guardando = signal(false);
  readonly errorDialogo = signal<string | null>(null);
  readonly rolesDisponibles = signal<IdentityRoleDto[]>([]);
  readonly verClave = signal(false);
  form: Formulario = this.formVacio();
  readonly opcionesOrg = signal<OpcionesDeOrganizacionDto | null>(null);
  readonly organizaciones = signal<OrganizacionResumenDto[]>([]);

  // --- Seguridad de la persona que se edita ---
  readonly seguridad = signal<SeguridadDeUsuarioDto | null>(null);
  readonly confirmarQuitar2fa = signal(false);
  readonly quitando2fa = signal(false);

  // --- Permisos individuales ---
  permisosVisibles = false;
  permisosDe: IdentityUserDto | null = null;

  ngOnInit(): void {
    this.buscar$
      .pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.pagina.set(0);
        this.cargar();
      });
    this.cargar();
  }

  alBuscar(): void {
    this.buscar$.next(this.busqueda);
  }

  cargar(): void {
    this.cargando.set(true);
    this.api
      .getList({
        filter: this.busqueda.trim() || undefined,
        sorting: 'userName',
        skipCount: this.pagina() * POR_PAGINA,
        maxResultCount: POR_PAGINA,
      })
      .pipe(
        // ABP no manda los roles en la lista. Son pocas personas por página y en una sola
        // organización: se piden en paralelo para poder enseñarlos en cada ficha.
        switchMap(r =>
          (r.items?.length
            ? forkJoin(
                r.items.map(u =>
                  this.api.getRoles(u.id!).pipe(
                    map(x => (x.items ?? []).map(rol => rol.name ?? '')),
                    catchError(() => of([] as string[])),
                  ),
                ),
              )
            : of([] as string[][])
          ).pipe(map(roles => ({ r, roles }))),
        ),
        // Y de una vez, a qué organización pertenece cada uno (los clientes).
        switchMap(({ r, roles }) =>
          (r.items?.length
            ? this.membresias.deUsuarios(r.items.map(u => u.id!)).pipe(catchError(() => of([])))
            : of([])
          ).pipe(map(membresias => ({ r, roles, membresias }))),
        ),
      )
      .subscribe({
        next: ({ r, roles, membresias }) => {
          const orgs = new Map(membresias.map(m => [m.userId, m.organizaciones]));
          this.filas.set(
            (r.items ?? []).map((usuario, i) => ({
              usuario,
              roles: ordenarRoles(roles[i], x => x),
              organizaciones: orgs.get(usuario.id!) ?? [],
            })),
          );
          this.total.set(r.totalCount ?? 0);
          this.cargando.set(false);
          this.error.set(null);
        },
        error: e => {
          this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de usuarios.'));
          this.cargando.set(false);
        },
      });
  }

  irAPagina(p: number): void {
    if (p < 0 || p >= this.paginas()) {
      return;
    }
    this.pagina.set(p);
    this.cargar();
  }

  nombreCompleto(u: IdentityUserDto): string {
    return [u.name, u.surname].filter(Boolean).join(' ') || u.userName || '';
  }

  iniciales(u: IdentityUserDto): string {
    const base = this.nombreCompleto(u).trim();
    return (
      base
        .split(/\s+/)
        .slice(0, 2)
        .map(p => p[0] ?? '')
        .join('')
        .toUpperCase() || '?'
    );
  }

  bloqueado(u: IdentityUserDto): boolean {
    return !!u.lockoutEnd && new Date(u.lockoutEnd).getTime() > Date.now();
  }

  esUnoMismo(u: IdentityUserDto): boolean {
    return u.id === this.idActual();
  }

  claseRol(rol: string): string {
    const nivel = nivelRol(rol);
    return nivel >= 2 ? 'ig-etiqueta-tinta' : nivel === 1 ? 'ig-etiqueta-acento' : '';
  }

  // --- Alta y edición ------------------------------------------------------------

  nuevo(): void {
    this.form = this.formVacio();
    this.seguridad.set(null);
    this.abrirDialogo();
  }

  editar(fila: Fila): void {
    const u = fila.usuario;
    this.form = {
      id: u.id ?? null,
      concurrencyStamp: u.concurrencyStamp,
      userName: u.userName ?? '',
      name: u.name ?? '',
      surname: u.surname ?? '',
      email: esCorreoProvisional(u.email) ? '' : (u.email ?? ''),
      sinCorreo: esCorreoProvisional(u.email),
      phoneNumber: u.phoneNumber ?? '',
      password: '',
      isActive: u.isActive,
      lockoutEnabled: u.lockoutEnabled,
      rol: null,
      rolesActuales: fila.roles,
      organizacionId: null,
      organizacionesActuales: fila.organizaciones,
    };
    this.cargarSeguridad(u);
    this.abrirDialogo();
  }

  private abrirDialogo(): void {
    this.errorDialogo.set(null);
    this.membresias.getOpciones().subscribe({
      next: o => {
        this.opcionesOrg.set(o);
        this.organizaciones.set(o.organizaciones);
      },
    });
    this.verClave.set(false);
    this.dialogo.set(true);
    if (this.puedeRoles && !this.rolesDisponibles().length) {
      this.api.getAssignableRoles().subscribe({
        next: r => {
          this.rolesDisponibles.set(ordenarRoles(r.items ?? [], x => x.name));
          this.elegirRolInicial();
        },
        error: () => this.rolesDisponibles.set([]),
      });
    } else {
      this.elegirRolInicial();
    }
  }

  /** Lo propio se gestiona en «Seguridad de tu cuenta»; aquí, solo de los demás. */
  private cargarSeguridad(u: IdentityUserDto): void {
    this.seguridad.set(null);
    this.confirmarQuitar2fa.set(false);
    if (!u.id || this.esUnoMismo(u)) {
      return;
    }
    this.seguridadApi.get(u.id).subscribe({ next: s => this.seguridad.set(s), error: () => this.seguridad.set(null) });
  }

  quitarSegundoFactor(): void {
    const id = this.form.id;
    if (!id) {
      return;
    }
    if (!this.confirmarQuitar2fa()) {
      this.confirmarQuitar2fa.set(true);
      return;
    }
    this.quitando2fa.set(true);
    this.seguridadApi.quitarSegundoFactor(id).subscribe({
      next: s => {
        this.quitando2fa.set(false);
        this.confirmarQuitar2fa.set(false);
        this.seguridad.set(s);
        this.avisos.success(`${this.form.userName} ya puede entrar solo con su contraseña.`, 'Verificación en dos pasos quitada');
      },
      error: e => {
        this.quitando2fa.set(false);
        this.avisos.error(mensajeDeError(e), 'No se pudo quitar');
      },
    });
  }

  /** Al editar, su rol más alto que se pueda asignar; al crear, «usuario» (el más bajo). */
  private elegirRolInicial(): void {
    const opciones = this.rolesDelFormulario().map(r => r.nombre);
    if (this.form.id) {
      this.form.rol = ordenarRoles(this.form.rolesActuales, x => x).find(r => opciones.includes(r)) ?? null;
    } else {
      this.form.rol = opciones.includes('usuario') ? 'usuario' : (opciones.at(-1) ?? null);
    }
  }

  /**
   * Los roles que se pueden elegir. El Developer (`admin`) solo aparece si la persona ya lo
   * es: a otro Developer no se le da de alta desde aquí, igual que no se le invita.
   */
  rolesDelFormulario(): { nombre: string; etiqueta: string; icono: string; texto: string }[] {
    return this.rolesDisponibles()
      .map(r => r.name ?? '')
      .filter(n => !!n && (nivelRol(n) < 3 || this.form.rolesActuales.includes(n)))
      .map(nombre => ({ nombre, etiqueta: etiquetaRol(nombre), ...describirRol(nombre) }));
  }

  /** Roles que tiene y quien edita no puede quitar: se enseñan, no se tocan. */
  rolesFijos(): string[] {
    const opciones = this.rolesDelFormulario().map(r => r.nombre);
    return this.form.rolesActuales.filter(r => !opciones.includes(r));
  }

  /**
   * ¿Queda como cliente? Mira el rol elegido y los que no se le pueden quitar, igual que
   * el backend. Un cliente entra DENTRO de una organización: sin ella no vería nada.
   */
  pideOrganizacion(): boolean {
    const nivel = Math.max(nivelRol(this.form.rol), ...this.rolesFijos().map(nivelRol));
    return necesitaOrganizacion(nivel, this.opcionesOrg());
  }

  /** Obligatoria al crear un cliente, o al editar uno que aún no pertenece a ninguna. */
  organizacionObligatoria(): boolean {
    return this.pideOrganizacion() && (!this.form.id || !this.form.organizacionesActuales.length);
  }

  anadirOrganizacion(o: OrganizacionResumenDto): void {
    this.organizaciones.set([...this.organizaciones(), o].sort((a, b) => a.nombre.localeCompare(b.nombre)));
  }

  /** Las que puede elegir al editar: las que aún no tiene. */
  organizacionesElegibles(): OrganizacionResumenDto[] {
    const ya = new Set(this.form.organizacionesActuales.map(o => o.id));
    return this.organizaciones().filter(o => !ya.has(o.id));
  }

  get formularioValido(): boolean {
    const f = this.form;
    const correoOk = f.sinCorreo || /\S+@\S+\.\S+/.test(f.email.trim());
    const organizacionOk = !this.organizacionObligatoria() || !!f.organizacionId;
    return !!f.userName.trim() && correoOk && (f.id !== null || f.password.length > 0) && organizacionOk;
  }

  guardar(): void {
    if (!this.formularioValido || this.guardando()) {
      return;
    }
    const f = this.form;
    const base = {
      userName: f.userName.trim(),
      name: f.name.trim() || undefined,
      surname: f.surname.trim() || undefined,
      email: f.sinCorreo ? this.correoProvisional(f.userName) : f.email.trim(),
      phoneNumber: f.phoneNumber.trim() || undefined,
      isActive: f.isActive,
      lockoutEnabled: f.lockoutEnabled,
      // El backend conserva los roles que quien edita no alcanza (FilterRolesByCurrentUserAsync).
      roleNames: f.rol ? [f.rol] : [],
      // La organización va en las propiedades extra del DTO de ABP (IgasIdentityUserAppService).
      extraProperties: this.pideOrganizacion() && f.organizacionId ? { OrganizacionId: f.organizacionId } : {},
    };

    this.guardando.set(true);
    this.errorDialogo.set(null);
    const peticion = f.id
      ? this.api.update(f.id, { ...base, password: f.password || undefined, concurrencyStamp: f.concurrencyStamp })
      : this.api.create({ ...base, password: f.password });

    peticion.subscribe({
      next: u => {
        this.guardando.set(false);
        this.dialogo.set(false);
        this.avisos.success(`${u.userName} ${f.id ? 'se actualizó' : 'ya tiene cuenta'}.`, f.id ? 'Guardado' : 'Usuario creado');
        this.cargar();
      },
      error: e => {
        this.guardando.set(false);
        this.errorDialogo.set(mensajeDeError(e, 'No se pudo guardar. Revisa los datos.'));
      },
    });
  }

  // --- Permisos y borrado --------------------------------------------------------

  abrirPermisos(u: IdentityUserDto): void {
    this.permisosDe = u;
    this.permisosVisibles = true;
  }

  eliminar(u: IdentityUserDto): void {
    this.confirmacion
      .warn(
        `${this.nombreCompleto(u)} ya no podrá entrar. Si solo quieres pausar su acceso, edítalo y desmarca «Activo»: así se conserva su historial.`,
        `¿Eliminar a ${u.userName}?`,
        { yesText: 'Eliminar', cancelText: 'Cancelar' },
      )
      .subscribe(estado => {
        if (estado !== Confirmation.Status.confirm) {
          return;
        }
        this.ocupado.set(u.id ?? null);
        this.api.delete(u.id!).subscribe({
          next: () => {
            this.ocupado.set(null);
            this.avisos.success(`${u.userName} se eliminó.`, 'Eliminado');
            if (this.filas().length === 1 && this.pagina() > 0) {
              this.pagina.update(p => p - 1);
            }
            this.cargar();
          },
          error: e => {
            this.ocupado.set(null);
            this.avisos.error(mensajeDeError(e), 'No se eliminó');
          },
        });
      });
  }

  /** Único por usuario, como exige Identity: «rafael@sin-correo.invalid». */
  private correoProvisional(userName: string): string {
    const local = userName.trim().toLowerCase().replace(/[^a-z0-9._-]/g, '') || 'usuario';
    return `${local}@${DOMINIO_PROVISIONAL}`;
  }

  private formVacio(): Formulario {
    return {
      id: null,
      userName: '',
      name: '',
      surname: '',
      email: '',
      sinCorreo: false,
      phoneNumber: '',
      password: '',
      isActive: true,
      lockoutEnabled: true,
      rol: null,
      rolesActuales: [],
      organizacionId: null,
      organizacionesActuales: [],
    };
  }
}
