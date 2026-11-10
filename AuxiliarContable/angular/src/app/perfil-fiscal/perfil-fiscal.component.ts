import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { chatEncendido } from '../shared/chat';
import {
  ClienteVisibleDto,
  DatoDelPerfilDto,
  EstadoDeDocumento,
  GrupoDeCampos,
  OrigenDelDato,
  PerfilFiscalDto,
  PerfilFiscalService,
  TipoDeCampo,
  etiquetaDeOrigen,
  valorLegible,
} from './perfil-fiscal.service';

const GRUPOS: { grupo: GrupoDeCampos; titulo: string }[] = [
  { grupo: GrupoDeCampos.Identificacion, titulo: 'Identificación' },
  { grupo: GrupoDeCampos.Fiscal, titulo: 'Situación fiscal' },
  { grupo: GrupoDeCampos.Operacion, titulo: 'Cómo opera' },
];

/**
 * El perfil fiscal de un cliente: la CSF (opcional) que lo prellena y cada dato con su origen.
 * Mobile first: una columna, acciones a todo el ancho; desde 768 px, dos columnas por dato.
 *
 * <p>Lo usa el cliente (su perfil) y el despacho (el de cualquier cliente). El cliente activo
 * va en la URL (`?cliente=`) y se ve SIEMPRE arriba: nunca hay duda de quién es el perfil.</p>
 */
@Component({
  selector: 'app-perfil-fiscal',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './perfil-fiscal.component.html',
  styleUrls: ['./perfil-fiscal.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PerfilFiscalComponent implements OnInit {
  private readonly api = inject(PerfilFiscalService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly Estado = EstadoDeDocumento;
  readonly TipoDeCampo = TipoDeCampo;
  readonly etiquetaDeOrigen = etiquetaDeOrigen;
  readonly valorLegible = valorLegible;

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  /** Sin chat (sin API de Claude) no se ofrece «preguntar al asistente». */
  readonly chat = chatEncendido();
  readonly clienteId = signal<string | null>(null);
  readonly perfil = signal<PerfilFiscalDto | null>(null);
  readonly cargando = signal(false);
  readonly subiendo = signal(false);
  readonly error = signal<string | null>(null);
  readonly editando = signal<string | null>(null);
  readonly guardando = signal<string | null>(null);
  valorEditado = '';

  readonly cliente = computed(() => this.clientes()?.find(c => c.id === this.clienteId()) ?? null);
  readonly grupos = computed(() => {
    const p = this.perfil();
    return p ? GRUPOS.map(g => ({ ...g, campos: p.campos.filter(c => c.grupo === g.grupo) })) : [];
  });
  readonly porConfirmar = computed(() => this.perfil()?.campos.filter(c => c.valor && !c.confirmado).length ?? 0);
  readonly deLaConstanciaPorConfirmar = computed(
    () => this.perfil()?.campos.filter(c => !c.confirmado && c.origen === OrigenDelDato.ExtraidoDeCsf).length ?? 0,
  );

  ngOnInit(): void {
    this.api.clientes().subscribe({
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
    this.perfil.set(null);
    this.editando.set(null);
    this.router.navigate([], { queryParams: { cliente: id }, replaceUrl: true });
    if (id) {
      this.cargar();
    }
  }

  cargar(): void {
    const id = this.clienteId();
    if (!id) {
      return;
    }
    this.cargando.set(true);
    this.api.obtener(id).subscribe({
      next: p => this.mostrar(p),
      error: e => {
        this.cargando.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo cargar el perfil.'));
      },
    });
  }

  subir(evento: Event): void {
    const entrada = evento.target as HTMLInputElement;
    const archivo = entrada.files?.[0];
    entrada.value = '';
    const id = this.clienteId();
    if (!archivo || !id) {
      return;
    }
    this.error.set(null);
    this.subiendo.set(true);
    this.api.subirCsf(id, archivo).subscribe({
      next: p => {
        this.subiendo.set(false);
        this.mostrar(p);
      },
      error: e => {
        this.subiendo.set(false);
        this.error.set(mensajeDeError(e, 'No se pudo subir la constancia.'));
      },
    });
  }

  confirmar(dato: DatoDelPerfilDto): void {
    this.accion(dato.clave, this.api.confirmar(this.clienteId()!, dato.clave));
  }

  confirmarConstancia(): void {
    this.accion('*', this.api.confirmarConstancia(this.clienteId()!));
  }

  corregir(dato: DatoDelPerfilDto): void {
    this.editando.set(dato.clave);
    this.valorEditado = this.paraEditar(dato);
  }

  guardar(dato: DatoDelPerfilDto): void {
    const valor = dato.tipo === TipoDeCampo.Lista
      ? JSON.stringify(this.valorEditado.split('\n').map(l => l.trim()).filter(Boolean))
      : this.valorEditado;
    this.accion(dato.clave, this.api.responder(this.clienteId()!, dato.clave, valor), () => this.editando.set(null));
  }

  /** Las listas de objetos (regímenes, obligaciones) salen de la CSF: no se editan a mano aquí. */
  esEditable(dato: DatoDelPerfilDto): boolean {
    return dato.tipo !== TipoDeCampo.Lista || dato.clave === 'bancos';
  }

  esSugerido(dato: DatoDelPerfilDto): boolean {
    return !!dato.valor && !dato.confirmado;
  }

  esDeLaConstancia(dato: DatoDelPerfilDto): boolean {
    return dato.origen === OrigenDelDato.ExtraidoDeCsf;
  }

  private paraEditar(dato: DatoDelPerfilDto): string {
    if (dato.tipo === TipoDeCampo.Lista) {
      try {
        return (JSON.parse(dato.valor ?? '[]') as string[]).join('\n');
      } catch {
        return '';
      }
    }
    return dato.valor ?? '';
  }

  private accion(clave: string, peticion: ReturnType<PerfilFiscalService['obtener']>, despues?: () => void): void {
    this.error.set(null);
    this.guardando.set(clave);
    peticion.subscribe({
      next: p => {
        this.guardando.set(null);
        this.mostrar(p);
        despues?.();
      },
      error: e => {
        this.guardando.set(null);
        this.error.set(mensajeDeError(e, 'No se pudo guardar.'));
      },
    });
  }

  private mostrar(p: PerfilFiscalDto): void {
    this.cargando.set(false);
    this.perfil.set(p);
  }
}
