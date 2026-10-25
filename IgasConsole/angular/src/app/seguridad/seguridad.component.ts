import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ToasterService } from '@abp/ng.theme.shared';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { SeguridadService } from './seguridad.service';

type Paso = 'ver' | 'editar' | 'codigo';

/**
 * «Seguridad de tu cuenta»: correo y teléfono verificados (para recuperar la cuenta y recibir
 * códigos) y la verificación en dos pasos. Cada dato se verifica con un código de un solo
 * uso; hasta verificarlo, no cambia nada.
 */
@Component({
  selector: 'app-seguridad',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './seguridad.component.html',
  styleUrls: ['./seguridad.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SeguridadComponent implements OnInit {
  private readonly api = inject(SeguridadService);
  private readonly avisos = inject(ToasterService);

  readonly estado = toSignal(this.api.estado$, { initialValue: null });
  readonly cargando = signal(true);
  readonly error = signal<string | null>(null);

  // Correo
  readonly pasoCorreo = signal<Paso>('ver');
  correo = '';
  codigoCorreo = '';
  readonly ocupadoCorreo = signal(false);
  readonly errorCorreo = signal<string | null>(null);

  // Teléfono
  readonly pasoTelefono = signal<Paso>('ver');
  telefono = '';
  codigoTelefono = '';
  readonly ocupadoTelefono = signal(false);
  readonly errorTelefono = signal<string | null>(null);

  readonly ocupado2fa = signal(false);

  readonly puedeActivar2fa = computed(() => {
    const e = this.estado();
    return !!e && ((e.correoConfirmado && e.correoDisponible) || (e.telefonoConfirmado && e.smsDisponible) || e.telegramVinculado);
  });

  readonly completo = computed(() => (this.estado()?.pendientes ?? 1) === 0);

  ngOnInit(): void {
    this.api.cargar().subscribe({
      next: e => {
        this.cargando.set(false);
        // Lo que falta, ya abierto: quien llega desde el aviso no tiene que buscar el botón.
        if (!e.correoConfirmado) {
          this.pasoCorreo.set('editar');
          this.correo = e.correo ?? '';
        }
        if (!e.telefono) {
          this.pasoTelefono.set('editar');
        }
      },
      error: err => {
        this.cargando.set(false);
        this.error.set(mensajeDeError(err, 'No se pudo cargar la seguridad de tu cuenta.'));
      },
    });
  }

  // --- Correo ---------------------------------------------------------------------

  cambiarCorreo(): void {
    this.correo = this.estado()?.correo ?? '';
    this.codigoCorreo = '';
    this.errorCorreo.set(null);
    this.pasoCorreo.set('editar');
  }

  enviarCodigoCorreo(): void {
    if (!this.correo.trim() || this.ocupadoCorreo()) {
      return;
    }
    this.ocupadoCorreo.set(true);
    this.errorCorreo.set(null);
    this.api.enviarCodigoCorreo(this.correo.trim()).subscribe({
      next: () => {
        this.ocupadoCorreo.set(false);
        this.codigoCorreo = '';
        this.pasoCorreo.set('codigo');
      },
      error: e => {
        this.ocupadoCorreo.set(false);
        this.errorCorreo.set(mensajeDeError(e));
      },
    });
  }

  confirmarCorreo(): void {
    if (this.codigoCorreo.trim().length < 6 || this.ocupadoCorreo()) {
      return;
    }
    this.ocupadoCorreo.set(true);
    this.errorCorreo.set(null);
    this.api.confirmarCorreo(this.correo.trim(), this.codigoCorreo.trim()).subscribe({
      next: () => {
        this.ocupadoCorreo.set(false);
        this.pasoCorreo.set('ver');
        this.avisos.success('Tu correo quedó verificado.', 'Listo');
      },
      error: e => {
        this.ocupadoCorreo.set(false);
        this.errorCorreo.set(mensajeDeError(e));
      },
    });
  }

  // --- Teléfono -------------------------------------------------------------------

  cambiarTelefono(): void {
    this.telefono = this.estado()?.telefono ?? '';
    this.codigoTelefono = '';
    this.errorTelefono.set(null);
    this.pasoTelefono.set('editar');
  }

  /** Con SMS manda un código; sin SMS, solo lo guarda (sin verificar). */
  enviarTelefono(): void {
    if (!this.telefono.trim() || this.ocupadoTelefono()) {
      return;
    }
    this.ocupadoTelefono.set(true);
    this.errorTelefono.set(null);

    if (!this.estado()?.smsDisponible) {
      this.api.guardarTelefono(this.telefono.trim()).subscribe({
        next: () => {
          this.ocupadoTelefono.set(false);
          this.pasoTelefono.set('ver');
          this.avisos.success('Guardamos tu teléfono.', 'Listo');
        },
        error: e => {
          this.ocupadoTelefono.set(false);
          this.errorTelefono.set(mensajeDeError(e));
        },
      });
      return;
    }

    this.api.enviarCodigoTelefono(this.telefono.trim()).subscribe({
      next: () => {
        this.ocupadoTelefono.set(false);
        this.codigoTelefono = '';
        this.pasoTelefono.set('codigo');
      },
      error: e => {
        this.ocupadoTelefono.set(false);
        this.errorTelefono.set(mensajeDeError(e));
      },
    });
  }

  confirmarTelefono(): void {
    if (this.codigoTelefono.trim().length < 6 || this.ocupadoTelefono()) {
      return;
    }
    this.ocupadoTelefono.set(true);
    this.errorTelefono.set(null);
    this.api.confirmarTelefono(this.telefono.trim(), this.codigoTelefono.trim()).subscribe({
      next: () => {
        this.ocupadoTelefono.set(false);
        this.pasoTelefono.set('ver');
        this.avisos.success('Tu teléfono quedó verificado.', 'Listo');
      },
      error: e => {
        this.ocupadoTelefono.set(false);
        this.errorTelefono.set(mensajeDeError(e));
      },
    });
  }

  // --- Verificación en dos pasos ----------------------------------------------------

  alternar2fa(activo: boolean): void {
    this.ocupado2fa.set(true);
    this.api.segundoFactor(activo).subscribe({
      next: () => {
        this.ocupado2fa.set(false);
        this.avisos.success(
          activo ? 'Desde ahora te pediremos un código al entrar.' : 'Ya no te pediremos código al entrar.',
          activo ? 'Verificación en dos pasos activada' : 'Verificación en dos pasos desactivada',
        );
        // Al activarla, los códigos de recuperación de una vez: es el momento en que la
        // persona está mirando y puede guardarlos.
        if (activo) {
          this.generarCodigos();
        }
      },
      error: e => {
        this.ocupado2fa.set(false);
        this.avisos.error(mensajeDeError(e), 'No se pudo cambiar');
      },
    });
  }

  // --- Códigos de recuperación -------------------------------------------------------

  readonly codigosNuevos = signal<string[] | null>(null);
  readonly ocupadoCodigos = signal(false);
  readonly copiados = signal(false);
  readonly confirmarRegenerar = signal(false);

  generarCodigos(): void {
    this.ocupadoCodigos.set(true);
    this.confirmarRegenerar.set(false);
    this.api.generarCodigosDeRecuperacion().subscribe({
      next: codigos => {
        this.ocupadoCodigos.set(false);
        this.copiados.set(false);
        this.codigosNuevos.set(codigos);
        this.api.cargar().subscribe();
      },
      error: e => {
        this.ocupadoCodigos.set(false);
        this.avisos.error(mensajeDeError(e), 'No se generaron');
      },
    });
  }

  /** Regenerar invalida los que tenga: si le quedan, se pide confirmación en la propia tarjeta. */
  pedirNuevosCodigos(): void {
    if ((this.estado()?.codigosDeRecuperacionRestantes ?? 0) > 0 && !this.confirmarRegenerar()) {
      this.confirmarRegenerar.set(true);
      return;
    }
    this.generarCodigos();
  }

  copiarCodigos(): void {
    const texto = (this.codigosNuevos() ?? []).join('\n');
    navigator.clipboard?.writeText(texto).then(
      () => this.copiados.set(true),
      () => this.avisos.warn('Tu navegador no dejó copiar. Descárgalos o anótalos.', 'No se copiaron'),
    );
  }

  descargarCodigos(): void {
    const codigos = this.codigosNuevos() ?? [];
    const texto = [
      'ZIA - Códigos de recuperación',
      `Generados el ${new Date().toLocaleString('es-MX')}`,
      '',
      'Cada código sirve UNA vez para entrar si no te llega el código por correo, SMS o Telegram.',
      'Guárdalos en un lugar seguro (un gestor de contraseñas, o impresos). No los compartas.',
      '',
      ...codigos,
      '',
    ].join('\r\n');
    const enlace = document.createElement('a');
    enlace.href = URL.createObjectURL(new Blob([texto], { type: 'text/plain;charset=utf-8' }));
    enlace.download = 'zia-codigos-de-recuperacion.txt';
    enlace.click();
    URL.revokeObjectURL(enlace.href);
  }

  cerrarCodigos(): void {
    this.codigosNuevos.set(null);
  }
}
