import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ConfigStateService, PermissionService } from '@abp/ng.core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CarteraService, SemaforoDeCliente } from '../cartera/cartera.service';
import { DocumentosService, MESES, NOMBRES_DE_TIPO } from '../documentos/documentos.service';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { Bloque, bloquesDeRespuesta } from './formato';
import { TIPOS_PARA_ADJUNTAR, TipoParaAdjuntar, tipoProbable } from './adjuntos';
import { ArchivoDelAsistenteDto, AsistenteService, CARTERA, ConversacionDto, MAX_TEXTO, MensajeDto, RolDelMensaje } from './asistente.service';

/** Una idea para empezar: el texto que se manda y el icono de la tarjeta. */
interface Sugerencia {
  icono: string;
  texto: string;
}

const SUGERENCIAS_CLIENTE: Sugerencia[] = [
  { icono: 'bi-person-lines-fill', texto: 'Ayúdame a completar mi perfil' },
  { icono: 'bi-list-check', texto: '¿Qué documentos me faltan este mes?' },
  { icono: 'bi-question-circle', texto: '¿Qué es la constancia de situación fiscal y dónde la saco?' },
];

const SUGERENCIAS_DESPACHO: Sugerencia[] = [
  { icono: 'bi-clipboard-data', texto: 'Resume el perfil de este cliente y lo que falta' },
  { icono: 'bi-envelope', texto: 'Redacta un mensaje para pedirle sus estados de cuenta' },
  { icono: 'bi-arrow-left-right', texto: '¿Cómo va la conciliación de este mes?' },
];

const SUGERENCIAS_CARTERA: Sugerencia[] = [
  { icono: 'bi-exclamation-octagon', texto: '¿Qué clientes están urgentes este mes?' },
  { icono: 'bi-bank', texto: '¿A quién le falta el estado de cuenta?' },
  { icono: 'bi-envelope', texto: 'Redacta un recordatorio para los que no han mandado facturas' },
];

/** Lo que se dice mientras el modelo contesta: no hay streaming y la espera debe sentirse viva. */
const FRASES_DE_ESPERA = ['Pensando…', 'Revisando la información…', 'Armando la respuesta…'];

/** Un dato del «pulso»: cifras deterministas (nunca del modelo) que se tocan para preguntar. */
export interface DatoDelPulso {
  icono: string;
  cifra: string;
  etiqueta: string;
  tono: 'bien' | 'atencion' | 'urgente';
  pregunta?: string;
  adjuntar?: TipoParaAdjuntar;
}

export interface Adjunto {
  archivo: File;
  tipo: TipoParaAdjuntar;
  estado: 'listo' | 'subiendo' | 'ok' | 'error';
  nota?: string;
}

/** El límite de nginx es 25 MB; se deja margen para el sobre multipart. */
const MAX_BYTES = 24 * 1024 * 1024;
const MAX_ADJUNTOS = 10;

/**
 * El chat con el asistente: la PORTADA de la consola (ruta `/`). Siempre sobre UN cliente
 * —o, para el equipo, sobre «Toda la cartera»—, que va en la URL (`?cliente=`) y se ve fijo
 * arriba como una pastilla que se toca para cambiar.
 *
 * <p>Sin mensajes, recibe con un saludo, el «pulso» del cliente (cifras deterministas que se
 * tocan para preguntar) e ideas para empezar. Con mensajes, es una conversación normal con la
 * caja pegada abajo. El clip adjunta documentos: se suben a la bóveda (o al perfil, la
 * constancia) y luego se le cuenta al asistente qué llegó, para que lo comente.</p>
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
  private readonly boveda = inject(DocumentosService);
  private readonly cartera = inject(CarteraService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly permisos = inject(PermissionService);
  private readonly config = inject(ConfigStateService);
  private readonly fondo = viewChild<ElementRef<HTMLElement>>('fondo');
  private readonly caja = viewChild<ElementRef<HTMLTextAreaElement>>('caja');
  private readonly selectorDeArchivos = viewChild<ElementRef<HTMLInputElement>>('archivos');

  readonly Rol = RolDelMensaje;
  readonly CARTERA = CARTERA;
  readonly maxTexto = MAX_TEXTO;
  readonly tiposParaAdjuntar = TIPOS_PARA_ADJUNTAR;

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  readonly conversacion = signal<ConversacionDto | null>(null);
  readonly pendiente = signal<string | null>(null);
  /** El turno que se está esperando: para reconocerlo en la conversación si se corta la petición. */
  private enCurso: { id: string; texto: string; antes: number } | null = null;
  protected readonly tactil = typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches;
  readonly error = signal<string | null>(null);
  readonly pulso = signal<DatoDelPulso[]>([]);
  readonly adjuntos = signal<Adjunto[]>([]);
  readonly selectorAbierto = signal(false);
  readonly filtro = signal('');
  readonly arrastrando = signal(false);
  readonly frase = signal(FRASES_DE_ESPERA[0]);
  readonly esEquipo = signal(false);
  readonly nombre = signal('');
  texto = '';

  readonly cliente = computed(() => this.clientes()?.find(c => c.id === this.clienteId()) ?? null);
  readonly mensajes = computed<MensajeDto[]>(() => {
    const lista = this.conversacion()?.mensajes ?? [];
    const enviando = this.pendiente();
    return enviando
      ? [...lista, { id: 'pendiente', rol: RolDelMensaje.Usuario, texto: enviando, creadoUtc: '', acciones: [] }]
      : lista;
  });
  readonly esCartera = computed(() => this.clienteId() === CARTERA);
  readonly sugerencias = computed(() =>
    this.esCartera() ? SUGERENCIAS_CARTERA : this.conversacion()?.esDelDespacho ? SUGERENCIAS_DESPACHO : SUGERENCIAS_CLIENTE,
  );
  readonly puedeAdjuntar = computed(() => !this.esCartera() && !!this.conversacion()?.puedeEditar);
  readonly subiendo = computed(() => this.adjuntos().some(a => a.estado === 'subiendo'));
  readonly ocupado = computed(() => !!this.pendiente() || this.subiendo());
  readonly clientesFiltrados = computed(() => {
    const f = normalizar(this.filtro());
    const lista = this.clientes() ?? [];
    return f ? lista.filter(c => normalizar(c.nombre).includes(f)) : lista;
  });

  readonly saludo = computed(() => {
    const h = new Date().getHours();
    const parte = h < 12 ? 'Buenos días' : h < 19 ? 'Buenas tardes' : 'Buenas noches';
    return parte;
  });

  readonly subtitulo = computed(() => {
    if (this.esCartera()) {
      return 'Tengo a la vista toda tu cartera. Pregúntame quién va atrasado o qué hay que pedir.';
    }
    if (this.conversacion()?.esDelDespacho) {
      return `Trabajemos en ${this.cliente()?.nombre ?? 'este cliente'}: su perfil, sus documentos y su conciliación.`;
    }
    return 'Soy tu auxiliar contable. Platícame o mándame tus documentos y yo los ordeno.';
  });

  constructor() {
    // La frase de espera rota mientras se espera respuesta; se apaga al destruir la vista.
    const reloj = setInterval(() => {
      if (this.pendiente()) {
        const i = FRASES_DE_ESPERA.indexOf(this.frase());
        this.frase.set(FRASES_DE_ESPERA[(i + 1) % FRASES_DE_ESPERA.length]);
      }
    }, 2500);
    inject(DestroyRef).onDestroy(() => clearInterval(reloj));
  }

  ngOnInit(): void {
    const usuario = this.config.getOne('currentUser') as { name?: string } | undefined;
    // Solo el nombre de pila: «Buenas tardes, Ana», no «Buenas tardes, Ana García López».
    this.nombre.set((usuario?.name || '').trim().split(/\s+/)[0] ?? '');
    this.esEquipo.set(this.permisos.getGrantedPolicy('AuxiliarContable.Organizaciones'));

    this.perfiles.clientes().subscribe({
      next: delServidor => {
        // El equipo puede platicar también sobre toda la cartera (solo el resumen; ver CLAUDE.md §1.2.11).
        const lista = this.esEquipo()
          ? [{ id: CARTERA, nombre: 'Toda la cartera', puedeEditar: false }, ...delServidor]
          : delServidor;
        this.clientes.set(lista);
        const pedido = this.ruta.snapshot.queryParamMap.get('cliente');
        // Sin cliente en la URL: el equipo entra a su cartera; un cliente, a su única organización.
        const elegido =
          lista.find(c => c.id === pedido) ?? (this.esEquipo() ? lista[0] : lista.length === 1 ? lista[0] : null);
        if (elegido) {
          this.elegir(elegido.id);
        } else if (lista.length > 1) {
          this.selectorAbierto.set(true);
        }
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string | null): void {
    this.selectorAbierto.set(false);
    this.filtro.set('');
    if (id === this.clienteId() && this.conversacion()) {
      return;
    }
    this.clienteId.set(id);
    this.conversacion.set(null);
    this.pulso.set([]);
    this.adjuntos.set([]);
    this.error.set(null);
    this.router.navigate([], { queryParams: { cliente: id }, replaceUrl: true });
    if (!id) {
      return;
    }
    this.api.obtener(id).subscribe({
      next: c => this.mostrar(c),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la conversación.')),
    });
    this.cargarPulso(id);
  }

  private recargarClientes(): void {
    this.perfiles.clientes().subscribe({
      next: delServidor => {
        const cartera = this.clientes()?.filter(c => c.id === CARTERA) ?? [];
        this.clientes.set([...cartera, ...delServidor]);
      },
      error: () => undefined,
    });
  }

  alternarSelector(): void {
    if (!this.ocupado()) {
      this.selectorAbierto.update(v => !v);
    }
  }

  async enviar(texto = this.texto): Promise<void> {
    const id = this.clienteId();
    let limpio = texto.trim();
    const porSubir = this.adjuntos().filter(a => a.estado === 'listo');
    if (!id || (!limpio && !porSubir.length) || this.ocupado()) {
      return;
    }
    this.error.set(null);

    if (porSubir.length) {
      const informe = await this.subirAdjuntos(id);
      if (!informe) {
        return;
      }
      limpio = limpio ? `${limpio}\n\n${informe}` : informe;
    }

    this.pendiente.set(limpio);
    this.frase.set(FRASES_DE_ESPERA[0]);
    this.texto = '';
    this.bajar();
    this.enCurso = { id, texto: limpio, antes: this.conversacion()?.mensajes.length ?? 0 };
    this.api.enviar(id, limpio).subscribe({
      next: c => this.alResponder(id, c),
      error: e => {
        // Sin respuesta del servidor (salió del navegador, se fue la señal): el turno sigue allá y
        // se guarda; se espera a que aparezca en la conversación en vez de darlo por perdido.
        if (e?.status === 0 && this.enCurso?.id === id) {
          this.esperarRespuesta(id, texto);
          return;
        }
        this.alFallar(e, texto);
      },
    });
  }

  /** Lo que se está bajando (el id del archivo o «mensaje:formato»), para no pedirlo dos veces. */
  readonly bajando = signal<string | null>(null);

  bajarArchivo(a: ArchivoDelAsistenteDto): void {
    this.bajar_(a.uso, a.mensajeId, a.uso, null, a.nombre);
  }

  bajarRespuesta(m: MensajeDto, formato: 'excel' | 'pdf'): void {
    this.bajar_(`${m.id}:${formato}`, m.id, null, formato, `Respuesta del asistente.${formato === 'excel' ? 'xlsx' : 'pdf'}`);
  }

  private bajar_(clave: string, mensajeId: string, uso: string | null, formato: string | null, nombre: string): void {
    if (this.bajando()) {
      return;
    }
    this.bajando.set(clave);
    this.api.descargar(mensajeId, uso, formato).subscribe({
      next: blob => {
        this.bajando.set(null);
        const url = URL.createObjectURL(blob);
        const enlace = document.createElement('a');
        enlace.href = url;
        enlace.download = nombre;
        enlace.click();
        setTimeout(() => URL.revokeObjectURL(url), 10_000);
      },
      error: e => {
        this.bajando.set(null);
        this.error.set(mensajeDeError(e, 'No se pudo descargar el archivo.'));
      },
    });
  }

  /** Al volver al navegador con una respuesta pendiente: quizá ya está guardada. */
  @HostListener('document:visibilitychange')
  alVolver(): void {
    const curso = this.enCurso;
    if (document.visibilityState === 'visible' && curso && this.pendiente()) {
      this.api.obtener(curso.id).subscribe({
        next: c => {
          if (this.yaRespondio(c)) {
            this.alResponder(curso.id, c);
          }
        },
        error: () => undefined,
      });
    }
  }

  /**
   * Enter manda en la computadora (Shift+Enter, salto de línea). En el celular Enter es salto de
   * línea, como en cualquier chat: se manda con la flecha.
   */
  alTeclear(evento: KeyboardEvent): void {
    if (evento.key === 'Enter' && !evento.shiftKey && !evento.isComposing && !this.tactil) {
      evento.preventDefault();
      this.enviar();
    }
  }

  private alResponder(id: string, c: ConversacionDto): void {
    if (!this.enCurso || this.enCurso.id !== id) {
      return;
    }
    this.enCurso = null;
    this.pendiente.set(null);
    this.adjuntos.set([]);
    if (this.clienteId() !== id) {
      return;
    }
    this.mostrar(c);
    this.cargarPulso(id);
    // Si dio de alta un cliente, que aparezca ya en la pastilla de arriba.
    if (c.mensajes.at(-1)?.acciones.some(a => a.startsWith('Dio de alta'))) {
      this.recargarClientes();
    }
  }

  private alFallar(e: unknown, texto: string): void {
    // Lo escrito vuelve a la caja: el servidor no guardó nada del turno. Los archivos sí
    // quedaron en la bóveda, así que no se ofrecen otra vez.
    this.enCurso = null;
    this.texto = texto.trim();
    this.adjuntos.set([]);
    this.pendiente.set(null);
    this.error.set(mensajeDeError(e, 'No se pudo enviar. Intenta de nuevo.'));
  }

  /** Revisa cada 5 s, hasta 3 minutos, si el turno ya quedó guardado. */
  private esperarRespuesta(id: string, texto: string, intento = 0): void {
    if (this.enCurso?.id !== id) {
      return;
    }
    if (intento >= 36) {
      this.alFallar(null, texto);
      return;
    }
    setTimeout(() => {
      if (this.enCurso?.id !== id) {
        return;
      }
      this.api.obtener(id).subscribe({
        next: c => (this.yaRespondio(c) ? this.alResponder(id, c) : this.esperarRespuesta(id, texto, intento + 1)),
        error: () => this.esperarRespuesta(id, texto, intento + 1),
      });
    }, 5000);
  }

  /** ¿La conversación ya trae la pregunta en curso con su respuesta al final? */
  private yaRespondio(c: ConversacionDto): boolean {
    const curso = this.enCurso;
    if (!curso || c.mensajes.length <= curso.antes || c.mensajes.at(-1)?.rol !== RolDelMensaje.Asistente) {
      return false;
    }
    const pregunta = [...c.mensajes].reverse().find(m => m.rol === RolDelMensaje.Usuario);
    return pregunta?.texto === curso.texto;
  }

  tocarPulso(dato: DatoDelPulso): void {
    if (dato.adjuntar !== undefined) {
      this.abrirArchivos(dato.adjuntar);
    } else if (dato.pregunta) {
      this.enviar(dato.pregunta);
    }
  }

  empezarDeNuevo(): void {
    const id = this.clienteId();
    if (!id || this.ocupado()) {
      return;
    }
    this.api.empezarDeNuevo(id).subscribe({
      next: c => this.mostrar(c),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo empezar de nuevo.')),
    });
  }

  // ---- Adjuntos -------------------------------------------------------------------------------

  private tipoPreferido: TipoParaAdjuntar | null = null;

  abrirArchivos(tipo: TipoParaAdjuntar | null = null): void {
    if (!this.puedeAdjuntar() || this.ocupado()) {
      return;
    }
    this.tipoPreferido = tipo;
    this.selectorDeArchivos()?.nativeElement.click();
  }

  alElegirArchivos(evento: Event): void {
    const entrada = evento.target as HTMLInputElement;
    this.agregar(Array.from(entrada.files ?? []));
    entrada.value = '';
    this.tipoPreferido = null;
  }

  alArrastrar(evento: DragEvent, dentro: boolean): void {
    if (!this.puedeAdjuntar() || !evento.dataTransfer?.types.includes('Files')) {
      return;
    }
    evento.preventDefault();
    this.arrastrando.set(dentro);
  }

  alSoltar(evento: DragEvent): void {
    if (!this.puedeAdjuntar()) {
      return;
    }
    evento.preventDefault();
    this.arrastrando.set(false);
    this.agregar(Array.from(evento.dataTransfer?.files ?? []));
  }

  cambiarTipo(indice: number, tipo: TipoParaAdjuntar): void {
    this.adjuntos.update(l => l.map((a, i) => (i === indice ? { ...a, tipo } : a)));
  }

  quitar(indice: number): void {
    this.adjuntos.update(l => l.filter((_, i) => i !== indice));
  }

  icono(a: Adjunto): string {
    if (a.archivo.name.toLowerCase().endsWith('.xml')) return 'bi-file-earmark-code';
    if (a.archivo.type.startsWith('image/')) return 'bi-file-earmark-image';
    return 'bi-file-earmark-pdf';
  }

  private agregar(archivos: File[]): void {
    if (!archivos.length) {
      return;
    }
    const nuevos: Adjunto[] = [];
    const rechazados: string[] = [];
    for (const archivo of archivos) {
      if (archivo.size > MAX_BYTES) {
        rechazados.push(`${archivo.name} pesa más de 24 MB`);
        continue;
      }
      const esXml = archivo.name.toLowerCase().endsWith('.xml');
      const tipo = this.tipoPreferido !== null && !esXml ? this.tipoPreferido : tipoProbable(archivo);
      nuevos.push({ archivo, tipo, estado: 'listo' });
    }
    const lista = [...this.adjuntos(), ...nuevos];
    if (lista.length > MAX_ADJUNTOS) {
      rechazados.push(`solo caben ${MAX_ADJUNTOS} archivos por mensaje`);
    }
    this.adjuntos.set(lista.slice(0, MAX_ADJUNTOS));
    this.error.set(rechazados.length ? `No se agregó todo: ${rechazados.join('; ')}.` : null);
    setTimeout(() => this.caja()?.nativeElement.focus());
  }

  /**
   * Sube los adjuntos uno por uno y devuelve el texto que se le cuenta al asistente, o `null` si
   * no se pudo subir ninguno (entonces no se manda nada y el motivo queda en cada archivo).
   */
  private async subirAdjuntos(id: string): Promise<string | null> {
    const renglones: string[] = [];
    let buenos = 0;
    const lista = this.adjuntos();
    for (let i = 0; i < lista.length; i++) {
      const a = lista[i];
      if (a.estado !== 'listo') {
        continue;
      }
      this.marcar(i, { estado: 'subiendo' });
      try {
        if (a.tipo === 'csf') {
          await firstValueFrom(this.perfiles.subirCsf(id, a.archivo));
          this.marcar(i, { estado: 'ok' });
          renglones.push(`- ${a.archivo.name} (constancia fiscal): subida al perfil`);
        } else {
          const r = await firstValueFrom(this.boveda.subir(id, a.tipo, a.archivo));
          const nota = r.esDuplicado ? 'ya estaba en la bóveda' : r.aviso || '';
          this.marcar(i, { estado: 'ok', nota });
          renglones.push(`- ${a.archivo.name} (${NOMBRES_DE_TIPO[r.documento.tipo].toLowerCase()})${nota ? ': ' + nota : ''}`);
        }
        buenos++;
      } catch (e) {
        const motivo = mensajeDeError(e, 'No se pudo subir.');
        this.marcar(i, { estado: 'error', nota: motivo });
        renglones.push(`- ${a.archivo.name}: no se pudo subir (${motivo})`);
      }
    }
    if (!buenos) {
      this.error.set('No se pudo subir ningún archivo. Revisa el motivo en cada uno.');
      return null;
    }
    const encabezado = buenos === 1 ? 'Te acabo de mandar un documento' : `Te acabo de mandar ${buenos} documentos`;
    return `${encabezado}:\n${renglones.join('\n')}`;
  }

  private marcar(indice: number, cambio: Partial<Adjunto>): void {
    this.adjuntos.update(l => l.map((a, i) => (i === indice ? { ...a, ...cambio } : a)));
  }

  // ---- Pulso ----------------------------------------------------------------------------------

  /** Cifras deterministas para la bienvenida. Si algo falla, el pulso simplemente no se pinta. */
  private cargarPulso(id: string): void {
    const ahora = new Date();
    const anio = ahora.getFullYear();
    const mes = ahora.getMonth() + 1;
    const nombreDelMes = MESES[ahora.getMonth()];

    if (id === CARTERA) {
      this.cartera.clientes(anio, mes).subscribe({
        next: lista => {
          if (this.clienteId() !== id) return;
          const cuantos = (s: SemaforoDeCliente) => lista.filter(c => c.semaforo === s).length;
          const urgentes = cuantos(SemaforoDeCliente.Urgente);
          const atencion = cuantos(SemaforoDeCliente.Atencion);
          const alDia = cuantos(SemaforoDeCliente.AlDia);
          this.pulso.set([
            { icono: 'bi-exclamation-octagon', cifra: `${urgentes}`, etiqueta: urgentes === 1 ? 'cliente urgente' : 'clientes urgentes', tono: urgentes ? 'urgente' : 'bien', pregunta: '¿Qué clientes están urgentes este mes y qué les falta?' },
            { icono: 'bi-hourglass-split', cifra: `${atencion}`, etiqueta: 'requieren atención', tono: atencion ? 'atencion' : 'bien', pregunta: '¿Qué clientes requieren atención y por qué?' },
            { icono: 'bi-check2-circle', cifra: `${alDia}`, etiqueta: `al día en ${nombreDelMes}`, tono: 'bien', pregunta: '¿Qué clientes están al día este mes?' },
          ]);
        },
        error: () => undefined,
      });
      return;
    }

    let perfil: DatoDelPulso | null = null;
    let documentos: DatoDelPulso | null = null;
    let constancia: DatoDelPulso | null = null;
    const publicar = () => {
      if (this.clienteId() === id) {
        this.pulso.set([perfil, documentos, constancia].filter((d): d is DatoDelPulso => !!d));
      }
    };

    this.perfiles.obtener(id).subscribe({
      next: p => {
        const llenos = p.campos.filter(c => c.valor?.trim()).length;
        const porcentaje = p.campos.length ? Math.round((llenos * 100) / p.campos.length) : 0;
        perfil = {
          icono: 'bi-person-lines-fill',
          cifra: `${porcentaje}%`,
          etiqueta: 'perfil fiscal',
          tono: porcentaje >= 100 ? 'bien' : porcentaje >= 50 ? 'atencion' : 'urgente',
          pregunta: porcentaje >= 100 ? 'Repasa conmigo el perfil fiscal' : 'Ayúdame a completar el perfil fiscal',
        };
        constancia = p.csf
          ? { icono: 'bi-file-earmark-check', cifra: 'Lista', etiqueta: 'constancia fiscal', tono: 'bien', pregunta: '¿Qué datos salieron de la constancia de situación fiscal?' }
          : p.puedeEditar
            ? { icono: 'bi-file-earmark-arrow-up', cifra: 'Falta', etiqueta: 'constancia fiscal · súbela', tono: 'urgente', adjuntar: 'csf' }
            : { icono: 'bi-file-earmark-x', cifra: 'Falta', etiqueta: 'constancia fiscal', tono: 'urgente', pregunta: '¿Cómo se saca la constancia de situación fiscal?' };
        publicar();
      },
      error: () => undefined,
    });

    this.boveda.resumen(id).subscribe({
      next: r => {
        const delMes = r.filter(x => x.anio === anio && x.mes === mes).reduce((s, x) => s + x.cantidad, 0);
        documentos = {
          icono: 'bi-folder2-open',
          cifra: `${delMes}`,
          etiqueta: `documentos de ${nombreDelMes}`,
          tono: delMes ? 'bien' : 'atencion',
          pregunta: `¿Qué documentos hay de ${nombreDelMes} y qué falta?`,
        };
        publicar();
      },
      error: () => undefined,
    });
  }

  // ---- Utilidades -----------------------------------------------------------------------------

  /** La respuesta en bloques (títulos, listas, tablas), una vez por mensaje. */
  private readonly formatos = new Map<string, Bloque[]>();

  formato(m: MensajeDto): Bloque[] {
    let b = this.formatos.get(m.id);
    if (!b) {
      b = bloquesDeRespuesta(m.texto);
      this.formatos.set(m.id, b);
    }
    return b;
  }

  hora(utc: string): string {
    return utc ? new Date(utc).toLocaleTimeString('es-MX', { hour: '2-digit', minute: '2-digit' }) : '';
  }

  // ---- Menú del mensaje (como en Telegram) ----------------------------------------------------

  /** El mensaje cuyo menú está abierto. */
  readonly menu = signal<MensajeDto | null>(null);
  /** El mensaje en modo «seleccionar texto»: ahí el toque largo vuelve a ser el del sistema. */
  readonly seleccionando = signal<string | null>(null);
  readonly aviso = signal<string | null>(null);
  readonly puedeCompartir = typeof navigator !== 'undefined' && typeof navigator.share === 'function';

  /** Lo que dura el «mantener presionado» antes de abrir el menú. */
  private static readonly PresionMs = 450;
  private presion?: ReturnType<typeof setTimeout>;
  private inicioDelToque = { x: 0, y: 0 };
  private relojDelAviso?: ReturnType<typeof setTimeout>;
  private abiertoPorPresion = false;

  alTocar(evento: TouchEvent, m: MensajeDto): void {
    if (m.id === 'pendiente' || this.seleccionando() === m.id || evento.touches.length !== 1) {
      return;
    }
    const t = evento.touches[0];
    this.inicioDelToque = { x: t.clientX, y: t.clientY };
    clearTimeout(this.presion);
    this.presion = setTimeout(() => {
      this.abiertoPorPresion = true;
      this.mostrarMenu(m);
    }, AsistenteComponent.PresionMs);
  }

  /** Si el dedo se mueve, es desplazamiento, no un toque largo. */
  alMover(evento: TouchEvent): void {
    const t = evento.touches[0];
    if (t && Math.hypot(t.clientX - this.inicioDelToque.x, t.clientY - this.inicioDelToque.y) > 10) {
      clearTimeout(this.presion);
    }
  }

  /**
   * ⚠️ Si el menú se abrió con el dedo aún abajo, al soltarlo el navegador fabrica un «click» en
   * ese punto, que ya cae sobre el velo y cerraba el menú en el acto. Cancelar el touchend lo evita.
   */
  alSoltarDedo(evento?: TouchEvent): void {
    clearTimeout(this.presion);
    if (this.abiertoPorPresion) {
      this.abiertoPorPresion = false;
      evento?.preventDefault();
    }
  }

  /** Clic derecho, el toque largo de Android (llega como `contextmenu`) o el botón «Más». */
  abrirMenu(evento: Event, m: MensajeDto): void {
    if (m.id === 'pendiente' || (evento.type === 'contextmenu' && this.seleccionando() === m.id)) {
      return;
    }
    evento.preventDefault();
    this.mostrarMenu(m);
  }

  @HostListener('document:keydown.escape')
  cerrarMenu(): void {
    this.menu.set(null);
  }

  async copiar(texto: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(texto);
    } catch {
      // Sin permiso de portapapeles (o sin HTTPS): el método viejo, que sigue funcionando en móviles.
      const area = document.createElement('textarea');
      area.value = texto;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      document.execCommand('copy');
      area.remove();
    }
    this.avisar('Copiado');
  }

  /** Deja el texto del globo seleccionado y con las asas del sistema para ajustarlo. */
  seleccionar(m: MensajeDto): void {
    this.menu.set(null);
    this.seleccionando.set(m.id);
    setTimeout(() => {
      const p = document.querySelector(`[data-mensaje="${m.id}"] .as-texto`);
      const seleccion = window.getSelection();
      if (p && seleccion) {
        const rango = document.createRange();
        rango.selectNodeContents(p);
        seleccion.removeAllRanges();
        seleccion.addRange(rango);
      }
    });
    this.avisar('Ajusta la selección y copia');
  }

  responder(m: MensajeDto): void {
    this.menu.set(null);
    const cita = m.texto.length > 160 ? `${m.texto.slice(0, 160).trimEnd()}…` : m.texto;
    this.texto = `«${cita}»\n${this.texto}`;
    this.enfocarCaja();
  }

  /** El historial no se reescribe (el asistente ya respondió a lo que decía): se edita una copia y se manda de nuevo. */
  editar(m: MensajeDto): void {
    this.menu.set(null);
    this.texto = m.texto;
    this.enfocarCaja();
    this.avisar('Edítalo y vuelve a enviarlo');
  }

  async compartir(m: MensajeDto): Promise<void> {
    this.menu.set(null);
    try {
      await navigator.share({ text: m.texto });
    } catch {
      // Cancelado por la persona: nada que hacer.
    }
  }

  private mostrarMenu(m: MensajeDto): void {
    clearTimeout(this.presion);
    this.seleccionando.set(null);
    navigator.vibrate?.(10);
    this.menu.set(m);
  }

  private enfocarCaja(): void {
    setTimeout(() => {
      const caja = this.caja()?.nativeElement;
      if (caja) {
        caja.focus();
        caja.setSelectionRange(caja.value.length, caja.value.length);
      }
    });
  }

  private avisar(texto: string): void {
    clearTimeout(this.relojDelAviso);
    this.aviso.set(texto);
    this.relojDelAviso = setTimeout(() => this.aviso.set(null), 1800);
  }

  /** Dos letras para la pastilla del cliente: «Panadería La Espiga» → «PL». */
  iniciales(nombre: string): string {
    return nombre
      .split(/\s+/)
      .filter(p => p.length > 2)
      .slice(0, 2)
      .map(p => p[0].toUpperCase())
      .join('');
  }

  private mostrar(c: ConversacionDto): void {
    this.conversacion.set(c);
    this.bajar();
  }

  private bajar(): void {
    setTimeout(() => this.fondo()?.nativeElement.scrollIntoView({ block: 'end', behavior: 'smooth' }));
  }
}

function normalizar(s: string): string {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}
