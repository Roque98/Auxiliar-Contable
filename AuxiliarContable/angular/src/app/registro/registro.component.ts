import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, ConfigStateService } from '@abp/ng.core';
import { toSignal } from '@angular/core/rxjs-interop';
import { tokenDelFragmento } from './token';
import { InvitacionPublicaDto, InvitacionesService, mensajeDeError } from '../invitaciones/invitaciones.service';
import { VERSION_DEL_AVISO } from '../privacidad/aviso';

type Paso = 'cargando' | 'invalida' | 'formulario' | 'lista';

/**
 * Alta por invitación: `/registro#t=TOKEN`, el enlace del correo.
 *
 * <p>El token va en el FRAGMENTO, que no viaja al servidor, y en cuanto se lee se borra de
 * la barra de direcciones: así no queda en el historial ni se comparte copiando la URL. A la
 * API se manda por POST, en el cuerpo.</p>
 */
@Component({
  selector: 'app-registro',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './registro.component.html',
  styleUrls: ['./registro.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegistroComponent implements OnInit {
  private readonly api = inject(InvitacionesService);
  private readonly auth = inject(AuthService);
  private readonly usuario = toSignal(inject(ConfigStateService).getOne$('currentUser'), { initialValue: null });

  readonly paso = signal<Paso>('cargando');
  readonly invitacion = signal<InvitacionPublicaDto | null>(null);
  readonly enviando = signal(false);
  readonly error = signal<string | null>(null);
  readonly verClave = signal(false);
  readonly conSesion = computed(() => !!this.usuario()?.isAuthenticated);

  private token: string | null = null;

  userName = '';
  name = '';
  surname = '';
  password = '';
  repetir = '';
  /** Casilla del aviso de privacidad: obligatoria (consentimiento expreso para datos financieros). */
  aceptaAviso = false;

  ngOnInit(): void {
    this.token = tokenDelFragmento(window.location.hash);
    if (window.location.hash) {
      history.replaceState(history.state, '', window.location.pathname);
    }

    if (!this.token) {
      this.paso.set('invalida');
      return;
    }

    this.api.consultar(this.token).subscribe({
      next: inv => {
        this.invitacion.set(inv);
        this.userName = inv.usuarioSugerido ?? '';
        this.paso.set(inv.vigente ? 'formulario' : 'invalida');
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo comprobar la invitación. Inténtalo de nuevo en un momento.'));
        this.paso.set('invalida');
      },
    });
  }

  /** Las reglas de contraseña de ABP por defecto, para avisar antes de enviar. */
  get reglas() {
    const c = this.password;
    return [
      { ok: c.length >= 6, texto: 'Al menos 6 caracteres' },
      { ok: /[A-Z]/.test(c) && /[a-z]/.test(c), texto: 'Mayúsculas y minúsculas' },
      { ok: /\d/.test(c), texto: 'Un número' },
      { ok: /[^A-Za-z0-9]/.test(c), texto: 'Un símbolo (p. ej. * ! #)' },
    ];
  }

  get coinciden(): boolean {
    return this.repetir.length > 0 && this.repetir === this.password;
  }

  get listo(): boolean {
    return !!this.userName.trim() && this.reglas.every(r => r.ok) && this.coinciden && this.aceptaAviso;
  }

  crear(): void {
    if (!this.token || !this.listo || this.enviando()) {
      return;
    }

    this.error.set(null);
    this.enviando.set(true);
    this.api
      .aceptar({
        token: this.token,
        userName: this.userName.trim(),
        name: this.name.trim() || undefined,
        surname: this.surname.trim() || undefined,
        password: this.password,
        versionDelAvisoAceptada: VERSION_DEL_AVISO,
      })
      .subscribe({
        next: () => {
          this.enviando.set(false);
          this.password = '';
          this.repetir = '';
          this.paso.set('lista');
        },
        error: e => {
          this.enviando.set(false);
          this.error.set(mensajeDeError(e, 'No se pudo crear la cuenta. Inténtalo de nuevo.'));
        },
      });
  }

  entrar(): void {
    this.auth.navigateToLogin();
  }

  salir(): void {
    this.auth.logout().subscribe();
  }
}
