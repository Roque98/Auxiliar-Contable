import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { AsistenteService, ConversacionDto, MAX_TEXTO, MensajeDto, RolDelMensaje } from './asistente.service';

const SUGERENCIAS_CLIENTE = [
  'Ayúdame a completar mi perfil',
  '¿Qué documentos me faltan?',
  '¿Qué es la constancia de situación fiscal y dónde la saco?',
];

const SUGERENCIAS_DESPACHO = [
  'Resume el perfil de este cliente y lo que falta',
  'Redacta un mensaje para pedirle sus estados de cuenta',
  '¿Qué documentos ha subido?',
];

/**
 * El chat con el asistente («platicar con tu contador»). Siempre sobre UN cliente, que va en la
 * URL (`?cliente=`) y se ve fijo arriba: el despacho nunca duda de con qué cliente está.
 *
 * <p>Mobile first: la conversación ocupa la pantalla y la caja de texto queda pegada abajo;
 * desde 768 px solo se limita el ancho de los globos.</p>
 *
 * <p>Las respuestas se pintan como TEXTO (interpolación, `white-space: pre-wrap`), nunca como
 * HTML: lo que escribe el modelo no es de fiar.</p>
 */
@Component({
  selector: 'app-asistente',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './asistente.component.html',
  styleUrls: ['./asistente.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AsistenteComponent implements OnInit {
  private readonly api = inject(AsistenteService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fondo = viewChild<ElementRef<HTMLElement>>('fondo');

  readonly Rol = RolDelMensaje;
  readonly maxTexto = MAX_TEXTO;

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly conversacion = signal<ConversacionDto | null>(null);
  readonly pendiente = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  texto = '';

  readonly cliente = computed(() => this.clientes()?.find(c => c.id === this.clienteId()) ?? null);
  readonly mensajes = computed<MensajeDto[]>(() => {
    const lista = this.conversacion()?.mensajes ?? [];
    const enviando = this.pendiente();
    return enviando
      ? [...lista, { id: 'pendiente', rol: RolDelMensaje.Usuario, texto: enviando, creadoUtc: '', acciones: [] }]
      : lista;
  });
  readonly sugerencias = computed(() => (this.conversacion()?.esDelDespacho ? SUGERENCIAS_DESPACHO : SUGERENCIAS_CLIENTE));

  ngOnInit(): void {
    this.perfiles.clientes().subscribe({
      next: lista => {
        this.clientes.set(lista);
        const pedido = this.ruta.snapshot.queryParamMap.get('cliente');
        const elegido = lista.find(c => c.id === pedido) ?? (lista.length === 1 ? lista[0] : null);
        if (elegido) {
          this.elegir(elegido.id);
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string | null): void {
    this.clienteId.set(id);
    this.conversacion.set(null);
    this.error.set(null);
    this.router.navigate([], { queryParams: { cliente: id }, replaceUrl: true });
    if (!id) {
      return;
    }
    this.api.obtener(id).subscribe({
      next: c => this.mostrar(c),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la conversación.')),
    });
  }

  enviar(texto = this.texto): void {
    const id = this.clienteId();
    const limpio = texto.trim();
    if (!id || !limpio || this.pendiente()) {
      return;
    }
    this.error.set(null);
    this.pendiente.set(limpio);
    this.texto = '';
    this.bajar();
    this.api.enviar(id, limpio).subscribe({
      next: c => {
        this.pendiente.set(null);
        this.mostrar(c);
      },
      error: e => {
        // Lo escrito vuelve a la caja: el servidor no guardó nada del turno.
        this.texto = limpio;
        this.pendiente.set(null);
        this.error.set(mensajeDeError(e, 'No se pudo enviar. Intenta de nuevo.'));
      },
    });
  }

  /** Enter manda; Shift+Enter hace salto de línea. */
  alTeclear(evento: KeyboardEvent): void {
    if (evento.key === 'Enter' && !evento.shiftKey && !evento.isComposing) {
      evento.preventDefault();
      this.enviar();
    }
  }

  empezarDeNuevo(): void {
    const id = this.clienteId();
    if (!id || this.pendiente()) {
      return;
    }
    this.api.empezarDeNuevo(id).subscribe({
      next: c => this.mostrar(c),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo empezar de nuevo.')),
    });
  }

  hora(utc: string): string {
    return utc ? new Date(utc).toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' }) : '';
  }

  private mostrar(c: ConversacionDto): void {
    this.conversacion.set(c);
    this.bajar();
  }

  private bajar(): void {
    setTimeout(() => this.fondo()?.nativeElement.scrollIntoView({ block: 'end', behavior: 'smooth' }));
  }
}
