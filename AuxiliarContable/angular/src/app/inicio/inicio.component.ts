import { ConfigStateService, PermissionService } from '@abp/ng.core';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CarteraService, SemaforoDeCliente } from '../cartera/cartera.service';
import { DocumentosService, MESES } from '../documentos/documentos.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { chatEncendido } from '../shared/chat';

interface Cifra {
  icono: string;
  cifra: string;
  etiqueta: string;
  tono: 'bien' | 'atencion' | 'urgente';
  ruta: string;
  params?: Record<string, string>;
}

interface Atajo {
  icono: string;
  titulo: string;
  texto: string;
  ruta: string;
  params?: Record<string, string>;
}

/**
 * La portada. Con el chat encendido, la portada ES el chat (se va a /asistente con los mismos
 * parámetros). Apagado, se trabaja por pantallas: saludo, las cifras del mes (las mismas del chat)
 * y atajos a cada pantalla según el rol. Sin guardia a propósito: sin sesión el layout pinta su
 * propia portada y esta ruta no se monta.
 */
@Component({
  selector: 'app-inicio',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './inicio.component.html',
  styleUrls: ['./inicio.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InicioComponent implements OnInit {
  private readonly config = inject(ConfigStateService);
  private readonly permisos = inject(PermissionService);
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly boveda = inject(DocumentosService);
  private readonly cartera = inject(CarteraService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly chat = chatEncendido();

  readonly esEquipo = this.permisos.getGrantedPolicy('AuxiliarContable.Organizaciones');
  readonly saludo: string;
  readonly cifras = signal<Cifra[]>([]);
  readonly cliente = signal<ClienteVisibleDto | null>(null);
  readonly atajos = computed<Atajo[]>(() => {
    const c = this.cliente();
    const deCliente = c ? { cliente: c.id } : undefined;
    const leyes: Atajo = { icono: 'bi-journal-text', titulo: 'Consultar leyes', texto: 'LISR, IVA, CFF, Miscelánea y NIF, con su cita.', ruta: '/leyes' };
    if (this.esEquipo) {
      return [
        { icono: 'bi-grid-3x3-gap', titulo: 'Cartera', texto: 'Todos los clientes del mes con su semáforo.', ruta: '/cartera' },
        { icono: 'bi-arrow-left-right', titulo: 'Cierre del mes', texto: 'Conciliar, aclarar lo que no tiene factura y cerrar.', ruta: '/conciliacion' },
        { icono: 'bi-folder2-open', titulo: 'Documentos', texto: 'Facturas, estados de cuenta y tickets de cada cliente.', ruta: '/documentos' },
        { icono: 'bi-person-lines-fill', titulo: 'Perfil fiscal', texto: 'RFC, régimen y datos de cada cliente.', ruta: '/perfil-fiscal' },
        leyes,
        { icono: 'bi-bank', titulo: 'Sentencias', texto: 'Cómo resuelven el TFJA y la SCJN, y qué hacer.', ruta: '/sentencias' },
      ];
    }
    return [
      { icono: 'bi-folder2-open', titulo: 'Documentos', texto: 'Sube tus facturas XML, estados de cuenta y tickets.', ruta: '/documentos', params: deCliente },
      { icono: 'bi-person-lines-fill', titulo: 'Perfil fiscal', texto: 'Sube tu constancia y revisa tus datos.', ruta: '/perfil-fiscal', params: deCliente },
      { icono: 'bi-arrow-left-right', titulo: 'Cierre del mes', texto: 'Cómo va tu mes y lo que el despacho te pregunta.', ruta: '/conciliacion', params: deCliente },
      { icono: 'bi-building', titulo: 'Mi organización', texto: 'Quién de tu empresa tiene acceso.', ruta: '/mi-organizacion' },
      leyes,
      { icono: 'bi-question-circle', titulo: 'Ayuda', texto: 'Cómo se usa cada pantalla.', ruta: '/ayuda' },
    ];
  });

  constructor() {
    const usuario = this.config.getOne('currentUser') as { name?: string; userName?: string } | undefined;
    const hora = new Date().getHours();
    const parte = hora < 12 ? 'Buenos días' : hora < 19 ? 'Buenas tardes' : 'Buenas noches';
    const nombre = (usuario?.name || '').trim().split(' ')[0];
    this.saludo = nombre ? `${parte}, ${nombre}` : parte;
  }

  ngOnInit(): void {
    if (this.chat()) {
      void this.router.navigate(['/asistente'], { queryParams: this.ruta.snapshot.queryParams, replaceUrl: true });
      return;
    }
    if (this.esEquipo) {
      this.cifrasDelDespacho();
    } else {
      this.perfiles.clientes().subscribe({
        next: lista => {
          const elegido = lista.find(c => c.id === this.ruta.snapshot.queryParamMap.get('cliente')) ?? lista[0] ?? null;
          this.cliente.set(elegido);
          if (elegido) {
            this.cifrasDelCliente(elegido.id);
          }
        },
        error: () => undefined,
      });
    }
  }

  private cifrasDelDespacho(): void {
    const ahora = new Date();
    this.cartera.clientes(ahora.getFullYear(), ahora.getMonth() + 1).subscribe({
      next: lista => {
        const cuantos = (s: SemaforoDeCliente) => lista.filter(c => c.semaforo === s).length;
        const urgentes = cuantos(SemaforoDeCliente.Urgente);
        const atencion = cuantos(SemaforoDeCliente.Atencion);
        this.cifras.set([
          { icono: 'bi-exclamation-octagon', cifra: `${urgentes}`, etiqueta: urgentes === 1 ? 'cliente urgente' : 'clientes urgentes', tono: urgentes ? 'urgente' : 'bien', ruta: '/cartera' },
          { icono: 'bi-hourglass-split', cifra: `${atencion}`, etiqueta: 'requieren atención', tono: atencion ? 'atencion' : 'bien', ruta: '/cartera' },
          { icono: 'bi-check2-circle', cifra: `${cuantos(SemaforoDeCliente.AlDia)}`, etiqueta: `al día en ${MESES[ahora.getMonth()]}`, tono: 'bien', ruta: '/cartera' },
        ]);
      },
      error: () => undefined,
    });
  }

  /** Las mismas cifras que enseñaba el chat: perfil, documentos del mes y constancia. */
  private cifrasDelCliente(id: string): void {
    const ahora = new Date();
    let perfil: Cifra | null = null;
    let documentos: Cifra | null = null;
    let constancia: Cifra | null = null;
    const publicar = () => this.cifras.set([perfil, documentos, constancia].filter((c): c is Cifra => !!c));
    const params = { cliente: id };

    this.perfiles.obtener(id).subscribe({
      next: p => {
        const llenos = p.campos.filter(c => c.valor?.trim()).length;
        const porcentaje = p.campos.length ? Math.round((llenos * 100) / p.campos.length) : 0;
        perfil = { icono: 'bi-person-lines-fill', cifra: `${porcentaje}%`, etiqueta: 'perfil fiscal', ruta: '/perfil-fiscal', params,
          tono: porcentaje >= 100 ? 'bien' : porcentaje >= 50 ? 'atencion' : 'urgente' };
        constancia = p.csf
          ? { icono: 'bi-file-earmark-check', cifra: 'Lista', etiqueta: 'constancia fiscal', tono: 'bien', ruta: '/perfil-fiscal', params }
          : { icono: 'bi-file-earmark-arrow-up', cifra: 'Falta', etiqueta: 'constancia fiscal · súbela', tono: 'urgente', ruta: '/perfil-fiscal', params };
        publicar();
      },
      error: () => undefined,
    });
    this.boveda.resumen(id).subscribe({
      next: r => {
        const delMes = r.filter(x => x.anio === ahora.getFullYear() && x.mes === ahora.getMonth() + 1).reduce((s, x) => s + x.cantidad, 0);
        documentos = { icono: 'bi-folder2-open', cifra: `${delMes}`, etiqueta: `documentos de ${MESES[ahora.getMonth()]}`,
          tono: delMes ? 'bien' : 'atencion', ruta: '/documentos', params };
        publicar();
      },
      error: () => undefined,
    });
  }
}
