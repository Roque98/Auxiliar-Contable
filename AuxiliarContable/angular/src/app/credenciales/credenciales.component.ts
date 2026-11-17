import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import {
  CredencialesDto,
  CredencialesService,
  NOMBRE_DE_ACCION,
  TipoDeCredencial,
  UsoDeCredencialDto,
  Verificacion,
} from './credenciales.service';

/** Lo que se escribió para desbloquear se olvida a los 5 minutos; una contraseña mostrada se oculta al minuto. */
const VIGENCIA_DESBLOQUEO_MS = 5 * 60 * 1000;
const VIGENCIA_REVELADA_MS = 60 * 1000;

/**
 * Mis credenciales: la e.firma (.cer, .key y su contraseña) y la CIEC del cliente, en un solo lugar. El cliente y el
 * despacho ven la vigencia; ver una contraseña o bajar el .key pide el NIP de 4 dígitos (o la contraseña de la
 * cuenta). Nada se guarda en el navegador: lo desbloqueado vive en memoria unos minutos.
 */
@Component({
  selector: 'app-credenciales',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './credenciales.component.html',
  styleUrls: ['./credenciales.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CredencialesComponent implements OnInit {
  private readonly api = inject(CredencialesService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destruir = inject(DestroyRef);

  readonly Tipo = TipoDeCredencial;
  readonly nombreDeAccion = NOMBRE_DE_ACCION;
  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly datos = signal<CredencialesDto | null>(null);
  readonly bitacora = signal<UsoDeCredencialDto[] | null>(null);
  readonly error = signal<string | null>(null);
  readonly aviso = signal<string | null>(null);
  readonly ocupado = signal(false);

  /** Contraseñas mostradas (por tipo), que se ocultan solas. */
  readonly reveladas = signal<Partial<Record<TipoDeCredencial, string>>>({});
  /** Qué acción espera el NIP: la pantalla pide desbloquear y al acertar la ejecuta. */
  readonly pendiente = signal<{ tipo: 'revelar'; credencial: TipoDeCredencial } | { tipo: 'key' } | null>(null);
  readonly editando = signal<'efirma' | 'ciec' | 'nip' | null>(null);

  // formulario de desbloqueo
  nip = '';
  contrasenaDeCuenta = '';
  usarContrasena = false;
  // formularios de alta
  cer: File | null = null;
  key: File | null = null;
  contrasenaEfirma = '';
  rfcCiec = '';
  contrasenaCiec = '';
  consentimiento = false;
  nuevoNip = '';
  contrasenaParaNip = '';

  private verificacion: Verificacion | null = null;
  private verificacionHasta = 0;
  private temporizadores: ReturnType<typeof setTimeout>[] = [];

  constructor() {
    this.destruir.onDestroy(() => {
      this.temporizadores.forEach(t => clearTimeout(t));
      this.verificacion = null;
    });
  }

  ngOnInit(): void {
    const q = this.ruta.snapshot.queryParamMap;
    this.perfiles.clientes().subscribe({
      next: lista => {
        this.clientes.set(lista);
        const elegido = lista.find(c => c.id === q.get('cliente')) ?? (lista.length === 1 ? lista[0] : null);
        this.clienteId.set(elegido?.id ?? null);
        this.cargar();
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string | null): void {
    this.clienteId.set(id);
    this.reveladas.set({});
    this.cargar();
  }

  cargar(): void {
    const id = this.clienteId();
    this.datos.set(null);
    this.bitacora.set(null);
    this.error.set(null);
    if (!id) {
      return;
    }
    void this.router.navigate([], { queryParams: { cliente: id }, replaceUrl: true });
    this.api.obtener(id).subscribe({
      next: d => {
        if (d.organizacionId !== this.clienteId()) {
          return;
        }
        this.datos.set(d);
        if (d.puedeAdministrar) {
          this.api.bitacora(id).subscribe({ next: b => this.bitacora.set(b), error: () => this.bitacora.set([]) });
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudieron cargar las credenciales.')),
    });
  }

  // ---- ver contraseña / bajar el .key (piden NIP)

  verContrasena(tipo: TipoDeCredencial): void {
    if (this.reveladas()[tipo]) {
      this.ocultar(tipo);
      return;
    }
    this.conVerificacion({ tipo: 'revelar', credencial: tipo });
  }

  descargarKey(): void {
    this.conVerificacion({ tipo: 'key' });
  }

  descargarCer(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.api.descargar(id, 'cer', {}).subscribe({
      next: a => this.guardarArchivo(a.nombre, a.contenidoBase64),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo descargar el .cer.')),
    });
  }

  /** Lo que se escribió en el desbloqueo: se usa para la acción pendiente y se recuerda unos minutos. */
  desbloquear(): void {
    const v: Verificacion = this.usarContrasena ? { contrasenaDeCuenta: this.contrasenaDeCuenta } : { nip: this.nip };
    this.ejecutar(v);
  }

  cancelarDesbloqueo(): void {
    this.pendiente.set(null);
    this.nip = '';
    this.contrasenaDeCuenta = '';
  }

  private conVerificacion(accion: NonNullable<ReturnType<typeof this.pendiente>>): void {
    this.error.set(null);
    this.pendiente.set(accion);
    if (this.verificacion && Date.now() < this.verificacionHasta) {
      this.ejecutar(this.verificacion);
    }
  }

  private ejecutar(v: Verificacion): void {
    const id = this.clienteId();
    const accion = this.pendiente();
    if (!id || !accion || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    const listo = () => {
      this.verificacion = v;
      this.verificacionHasta = Date.now() + VIGENCIA_DESBLOQUEO_MS;
      this.pendiente.set(null);
      this.nip = '';
      this.contrasenaDeCuenta = '';
      this.ocupado.set(false);
      this.recargarBitacora();
    };
    const fallo = (e: unknown) => {
      this.verificacion = null;
      this.error.set(mensajeDeError(e, 'No se pudo verificar.'));
      this.ocupado.set(false);
    };
    if (accion.tipo === 'revelar') {
      this.api.revelar(id, accion.credencial, v).subscribe({
        next: r => {
          this.reveladas.update(x => ({ ...x, [accion.credencial]: r.contrasena }));
          this.temporizadores.push(setTimeout(() => this.ocultar(accion.credencial), VIGENCIA_REVELADA_MS));
          listo();
        },
        error: fallo,
      });
    } else {
      this.api.descargar(id, 'key', v).subscribe({
        next: a => {
          this.guardarArchivo(a.nombre, a.contenidoBase64);
          listo();
        },
        error: fallo,
      });
    }
  }

  ocultar(tipo: TipoDeCredencial): void {
    this.reveladas.update(x => {
      const nuevo = { ...x };
      delete nuevo[tipo];
      return nuevo;
    });
  }

  copiar(texto: string): void {
    navigator.clipboard?.writeText(texto).catch(() => undefined);
  }

  private guardarArchivo(nombre: string, base64: string): void {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = nombre;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  // ---- alta y cambios

  abrir(que: 'efirma' | 'ciec' | 'nip'): void {
    this.editando.set(this.editando() === que ? null : que);
    this.error.set(null);
    this.aviso.set(null);
    this.consentimiento = false;
    this.rfcCiec = this.datos()?.ciec?.rfc ?? this.datos()?.efirma?.rfc ?? '';
  }

  elegirArchivo(evento: Event, cual: 'cer' | 'key'): void {
    const archivo = (evento.target as HTMLInputElement).files?.[0] ?? null;
    if (cual === 'cer') {
      this.cer = archivo;
    } else {
      this.key = archivo;
    }
  }

  async guardarEfirma(): Promise<void> {
    const id = this.clienteId();
    if (!id || !this.cer || !this.key || !this.contrasenaEfirma || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    const [cer, key] = await Promise.all([this.base64(this.cer), this.base64(this.key)]);
    this.api.guardarEfirma(id, cer, key, this.contrasenaEfirma, this.consentimiento).subscribe({
      next: d => this.guardado(d, 'La e.firma quedó guardada: la contraseña abre la llave y la llave es de ese certificado.'),
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo guardar la e.firma.'));
        this.ocupado.set(false);
      },
    });
  }

  guardarCiec(): void {
    const id = this.clienteId();
    if (!id || !this.rfcCiec || !this.contrasenaCiec || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    this.api.guardarCiec(id, this.rfcCiec, this.contrasenaCiec, this.consentimiento).subscribe({
      next: d => this.guardado(d, 'La CIEC quedó guardada.'),
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo guardar la CIEC.'));
        this.ocupado.set(false);
      },
    });
  }

  borrar(tipo: TipoDeCredencial): void {
    const id = this.clienteId();
    if (!id || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.api.borrar(id, tipo).subscribe({
      next: d => this.guardado(d, tipo === TipoDeCredencial.Efirma ? 'Se borró la e.firma.' : 'Se borró la CIEC.'),
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo borrar.'));
        this.ocupado.set(false);
      },
    });
  }

  ponerNip(): void {
    if (!/^\d{4}$/.test(this.nuevoNip) || !this.contrasenaParaNip || this.ocupado()) {
      return;
    }
    this.ocupado.set(true);
    this.error.set(null);
    this.api.ponerNip(this.nuevoNip, this.contrasenaParaNip).subscribe({
      next: () => {
        this.nuevoNip = '';
        this.contrasenaParaNip = '';
        this.verificacion = null;
        this.editando.set(null);
        this.aviso.set('Tu NIP quedó guardado. Te lo pediremos para ver contraseñas o bajar la llave.');
        this.ocupado.set(false);
        this.cargar();
      },
      error: e => {
        this.error.set(mensajeDeError(e, 'No se pudo guardar el NIP.'));
        this.ocupado.set(false);
      },
    });
  }

  private guardado(d: CredencialesDto, mensaje: string): void {
    this.datos.set(d);
    this.editando.set(null);
    this.cer = this.key = null;
    this.contrasenaEfirma = this.contrasenaCiec = '';
    this.consentimiento = false;
    this.aviso.set(mensaje);
    this.ocupado.set(false);
    this.recargarBitacora();
  }

  private recargarBitacora(): void {
    const id = this.clienteId();
    if (id && this.datos()?.puedeAdministrar) {
      this.api.bitacora(id).subscribe({ next: b => this.bitacora.set(b), error: () => undefined });
    }
  }

  private base64(archivo: File): Promise<string> {
    return new Promise((resolver, rechazar) => {
      const lector = new FileReader();
      lector.onload = () => resolver(String(lector.result).split(',', 2)[1] ?? '');
      lector.onerror = () => rechazar(lector.error);
      lector.readAsDataURL(archivo);
    });
  }
}
