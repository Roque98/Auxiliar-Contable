import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, NgZone, OnInit, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { ConfigStateService, CoreModule, LocalizationService } from '@abp/ng.core';
import { EsqueletoComponent } from '../shared/esqueleto.component';
import { MiCuentaDto, MiCuentaService } from './mi-cuenta.service';
import { ProfilePictureDto, ProfilePictureService } from '../profile-picture/profile-picture.service';

/**
 * «Mi cuenta»: la pantalla que faltaba.
 *
 * <p><b>El problema, dicho por quien lo usa:</b> «se ve vacío y poco profesional, y no
 * contamos con información del usuario». Y era literal — el grupo del menú tenía cinco
 * entradas (foto, Telegram, sesiones, avisos, claves) y cada una abría <b>una sola tarjeta
 * en medio de una pantalla en blanco</b>. En ningún sitio se decía quién eras, desde cuándo,
 * con qué roles, ni si tu cuenta estaba protegida.</p>
 *
 * <p><b>El patrón que siguen las aplicaciones que hacen esto bien</b> —GitHub, Google,
 * Stripe, Atlassian— es el mismo en las cuatro: <b>una página de cuenta</b> que empieza
 * identificando a la persona (avatar, nombre, usuario, correo, roles, antigüedad) y debajo
 * agrupa el resto por <b>áreas</b>: datos personales, seguridad, integraciones,
 * desarrollador. Nunca cinco entradas hermanas en la navegación principal, porque desde
 * fuera no se ve qué las une ni cuál abrir primero.</p>
 *
 * <p>Aquí se copia esa forma sin inventar nada nuevo: la identidad arriba, y cada área como
 * una tarjeta que <b>dice su estado</b> —«2 sesiones», «sin segundo factor»— y lleva a la
 * pantalla que ya existía. Ninguna de esas pantallas se ha tocado: siguen siendo la que
 * hace el trabajo, y esta es la portada que las ordena.</p>
 *
 * <p>⚠️ <b>El estado en la tarjeta no es adorno.</b> Una tarjeta que solo dijera «Segundo
 * factor» obliga a entrar para saber si lo tienes puesto; diciendo «desactivado» en ámbar,
 * la respuesta está en la portada. Es la diferencia entre un índice y un panel.</p>
 */
@Component({
  selector: 'app-mi-cuenta',
  standalone: true,
  imports: [CommonModule, RouterModule, CoreModule, EsqueletoComponent],
  templateUrl: './mi-cuenta.component.html',
  styleUrls: ['./mi-cuenta.component.scss'],
})
export class MiCuentaComponent implements OnInit {
  private readonly config = inject(ConfigStateService);
  private readonly servicio = inject(MiCuentaService);
  private readonly fotos = inject(ProfilePictureService);
  private readonly textos = inject(LocalizationService);
  private readonly zona = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  cargando = true;
  datos?: MiCuentaDto;
  foto?: string;

  /**
   * ⚠️ La identidad NO se pide a la API: ya está en la configuración que el SPA descarga al
   * arrancar. Pedirla otra vez daría dos fuentes para el mismo dato y un instante en el que
   * la pantalla se contradice — además de una llamada de más en la primera que se abre.
   */
  get usuario() {
    return this.config.getOne('currentUser') as {
      userName?: string;
      name?: string;
      surName?: string;
      email?: string;
      emailVerified?: boolean;
    } | null;
  }

  /** Nombre completo si lo hay; si no, el de usuario. Nunca una cadena vacía. */
  get nombre(): string {
    const u = this.usuario;
    const completo = [u?.name, u?.surName].filter(Boolean).join(' ').trim();
    return completo || u?.userName || '';
  }

  /**
   * Iniciales para el avatar cuando no hay foto.
   * <para>Sale del nombre completo y, si no lo hay, del usuario: dos letras siempre, porque
   * un círculo vacío se lee como un fallo de carga.</para>
   */
  get iniciales(): string {
    const partes = this.nombre.split(/\s+/).filter(Boolean);
    const letras = partes.length >= 2 ? partes[0][0] + partes[1][0] : this.nombre.slice(0, 2);
    return letras.toUpperCase();
  }

  /** §5.1: todo cambio que se pinte, dentro de la zona. */
  private aplicar(cambio: () => void): void {
    this.zona.run(() => {
      cambio();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.servicio.get().subscribe({
      next: d =>
        this.aplicar(() => {
          this.datos = d;
          this.cargando = false;
        }),
      error: () => this.aplicar(() => (this.cargando = false)),
    });

    // La foto va aparte y su fallo se traga: sin ella se pintan las iniciales, que es un
    // resultado correcto y no un error que enseñar.
    //
    // ⚠️ Se usa `cargar()`, que además publica en `foto$` — el mismo canal del que bebe el
    // avatar de la barra superior. Pidiéndola por otro camino, esta pantalla y la barra
    // podrían enseñar fotos distintas tras un cambio.
    this.fotos.cargar().subscribe({
      next: (r: ProfilePictureDto) => this.aplicar(() => (this.foto = r.dataUrl ?? undefined)),
      error: () => this.aplicar(() => (this.foto = undefined)),
    });
  }

  /**
   * Antigüedad en palabras. Un `toLocaleDateString` dice «12/3/2024» y obliga a restar
   * mentalmente; lo que se quiere saber es si la cuenta es de ayer o de hace dos años.
   */
  desdeHace(iso?: string): string {
    if (!iso) {
      return '—';
    }

    const dias = Math.floor((Date.now() - new Date(iso).getTime()) / 86400000);
    if (dias < 1) {
      return this.textos.instant('::Cuenta:Hoy');
    }
    if (dias < 60) {
      return `${dias} ${this.textos.instant('::Cuenta:Dias')}`;
    }
    if (dias < 730) {
      return `${Math.round(dias / 30)} ${this.textos.instant('::Cuenta:Meses')}`;
    }
    return `${Math.round(dias / 365)} ${this.textos.instant('::Cuenta:Anios')}`;
  }

  fecha(iso?: string | null): string {
    return iso
      ? new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
      : '—';
  }
}
