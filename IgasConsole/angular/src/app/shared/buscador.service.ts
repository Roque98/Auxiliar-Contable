import { PermissionService, RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

export interface Resultado {
  /** Grupo bajo el que se agrupa en la lista. Ya localizado. */
  grupo: string;
  etiqueta: string;
  /** Segunda línea, opcional: distingue dos resultados que se llaman parecido. */
  detalle?: string;
  ruta: string;
  icono: string;
}

/** Una pantalla a la que se puede saltar. `policy` vacío = la ve cualquiera con sesión. */
interface Destino {
  clave: string;
  ruta: string;
  icono: string;
  policy?: string;
}

/**
 * Lo que alimenta la paleta de comandos (Ctrl+K).
 *
 * <p>Dos fuentes con caracteres distintos:</p>
 * <ul>
 *   <li><b>Las pantallas</b>, que están aquí escritas y se filtran por permiso <b>en el
 *   cliente</b>. Son inmediatas: no hay red de por medio, así que la lista responde a cada
 *   tecla sin esperar a nadie.</li>
 *   <li><b>Los datos</b> —inquilinos, usuarios, productos—, que sí piden a la API y solo
 *   cuando hay algo escrito.</li>
 * </ul>
 *
 * <p>⚠️ <b>Cada fuente de datos se pide SOLO si su permiso está concedido</b>, y no se pide y
 * se ignora el 403. Un usuario sin esos permisos generaría tres peticiones fallidas por cada
 * búsqueda: ruido en el registro de auditoría y, peor, en la proporción de peticiones fallidas
 * por IP, que es la señal con la que el bloqueo automático decide a quién vetar (§6.15). Una
 * paleta de comandos se usa muchas veces al día — es justo lo que no conviene que parezca un
 * escaneo.</p>
 *
 * <p>⚠️ Y los fallos se tragan (<c>catchError</c>): si el listado de usuarios falla, la paleta
 * tiene que seguir enseñando las pantallas. Un buscador que se queda en blanco porque una de
 * sus tres fuentes tuvo un mal momento es peor que uno incompleto.</p>
 */
@Injectable({ providedIn: 'root' })
export class BuscadorService {
  private readonly rest = inject(RestService);
  private readonly permisos = inject(PermissionService);

  /** Cuántos resultados por fuente. Más no caben en pantalla sin hacer scroll. */
  private static readonly Tope = 5;

  private static readonly Destinos: Destino[] = [
    { clave: '::Menu:Home', ruta: '/', icono: 'bi-house' },
    { clave: '::Menu:Organizaciones', ruta: '/organizaciones', icono: 'bi-building', policy: 'IgasConsole.Organizaciones' },
    { clave: '::Menu:Actividad', ruta: '/actividad', icono: 'bi-clock-history', policy: 'IgasConsole.Actividad' },
    { clave: '::Menu:SystemStatus', ruta: '/system-status', icono: 'bi-activity', policy: 'IgasConsole.SystemStatus' },
    { clave: '::Menu:Metrics', ruta: '/metrics', icono: 'bi-graph-up', policy: 'IgasConsole.Metrics' },
    { clave: '::Menu:Jobs', ruta: '/jobs', icono: 'bi-list-task', policy: 'IgasConsole.Jobs' },
    { clave: '::Menu:Security', ruta: '/security', icono: 'bi-shield-lock', policy: 'IgasConsole.Security.BlockedIps' },
    { clave: '::Menu:Branding', ruta: '/branding', icono: 'bi-palette', policy: 'IgasConsole.Branding' },
    { clave: '::Menu:ApiKeys', ruta: '/api-keys', icono: 'bi-key', policy: 'IgasConsole.ApiKeys' },
    { clave: '::Menu:Notifications', ruta: '/notifications', icono: 'bi-bell' },
    { clave: '::Menu:Sessions', ruta: '/sessions', icono: 'bi-laptop' },
    { clave: '::Menu:ProfilePicture', ruta: '/profile-picture', icono: 'bi-person-circle' },
    { clave: '::Menu:Telegram', ruta: '/telegram', icono: 'bi-chat-dots' },
    { clave: '::Menu:Users', ruta: '/identity/users', icono: 'bi-people', policy: 'AbpIdentity.Users' },
    { clave: '::Menu:Roles', ruta: '/identity/roles', icono: 'bi-person-badge', policy: 'AbpIdentity.Roles' },
  ];

  /** Las pantallas que este usuario puede abrir, ya traducidas por quien llame. */
  destinosPermitidos(): Destino[] {
    return BuscadorService.Destinos.filter(
      d => !d.policy || this.permisos.getGrantedPolicy(d.policy),
    );
  }

  /**
   * Lo que hay que preguntarle al servidor. Devuelve una sola corriente con todo junto para
   * que la pantalla no tenga que orquestar tres suscripciones y sus estados de carga.
   */
  buscarDatos(texto: string, grupos: { usuarios: string }): Observable<Resultado[]> {
    const q = texto.trim();

    if (q.length < 2) {
      // Con una letra, cualquier búsqueda devuelve casi todo: no informa y cuesta tres
      // peticiones por pulsación.
      return of([]);
    }

    const fuentes: Observable<Resultado[]>[] = [];

    if (this.permisos.getGrantedPolicy('AbpIdentity.Users')) {
      fuentes.push(
        this.rest
          .request<void, { items?: { id: string; userName: string; email: string }[] }>(
            {
              method: 'GET',
              url: '/api/identity/users',
              params: { filter: q, maxResultCount: BuscadorService.Tope },
            },
            { apiName: 'Default', skipHandleError: true },
          )
          .pipe(
            map(r =>
              (r?.items ?? []).map(u => ({
                grupo: grupos.usuarios,
                etiqueta: u.userName,
                detalle: u.email,
                ruta: '/identity/users',
                icono: 'bi-person',
              })),
            ),
            catchError(() => of([])),
          ),
      );
    }

    if (fuentes.length === 0) {
      return of([]);
    }

    return forkJoin(fuentes).pipe(
      map(listas => listas.flat()),
      catchError(() => of([])),
    );
  }
}
