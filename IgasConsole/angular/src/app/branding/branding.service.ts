import { ConfigStateService, LocalizationService, RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { urlDelLogo } from './logo-url';

/** Lo que devuelve la pantalla de administración. Exige el permiso `IgasConsole.Branding`. */
export interface BrandingDto {
  /** Lo que ha puesto ESTE inquilino, o null si hereda el de la instalación. */
  appName?: string | null;
  /** Lo que se pinta hoy: el propio si lo hay, y si no, el heredado. */
  effectiveAppName: string;
  /** El que quedaría al vaciar el campo: el de la instalación, o el de serie. */
  inheritedAppName: string;
  accentColor: string;
  hasLogo: boolean;
  logoVersion?: string | null;
  logoUrl?: string | null;
  logoSizeBytes: number;
  /** true cuando quien edita es el host: lo que guarde es el valor por defecto de todos. */
  isHost: boolean;
}

export interface UpdateBrandingDto {
  appName?: string | null;
  accentColor?: string | null;
}

/**
 * La marca tal y como se está pintando ahora mismo.
 *
 * Sale de los AJUSTES que ya vienen en `/api/abp/application-configuration`, no de una
 * llamada propia. Eso importa por dos razones: no cuesta ninguna petición extra, y lo puede
 * leer **cualquiera** — el logo y el nombre los ve todo el mundo, mientras que el servicio
 * de administración exige permiso.
 */
export interface MarcaActual {
  /** Ya resuelto: el del inquilino si lo hay, y si no el de la instalación. Nunca vacío. */
  appName: string;
  accentColor: string | null;
  logoUrl: string | null;
}

const CLAVE_NOMBRE = 'IgasConsole.Branding.AppName';
const CLAVE_COLOR = 'IgasConsole.Branding.AccentColor';
const CLAVE_VERSION = 'IgasConsole.Branding.LogoVersion';

@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly rest = inject(RestService);
  private readonly config = inject(ConfigStateService);
  private readonly textos = inject(LocalizationService);
  private readonly baseUrl = '/api/app/tenant-branding';

  /**
   * Emite cada vez que cambia la configuración de la aplicación — lo que incluye entrar,
   * salir y cambiar de inquilino. Por eso basta suscribirse una vez al arrancar: no hace
   * falta detectar el login por separado.
   */
  readonly marca$: Observable<MarcaActual> = this.config.getSettings$('IgasConsole.Branding').pipe(
    map(ajustes => ({
      // El respaldo se aplica AQUÍ y no en cada pantalla: si no, una pantalla enseñaría el
      // nombre heredado y otra un hueco, y las dos parecerían correctas por separado. Es el
      // mismo respaldo que usa el servidor (el texto localizado `AppName`), para que la
      // barra lateral y la página de login no puedan decir cosas distintas.
      appName: this.oNulo(ajustes[CLAVE_NOMBRE]) ?? this.textos.instant('::AppName'),
      accentColor: this.oNulo(ajustes[CLAVE_COLOR]),
      // El inquilino sale de la misma respuesta que los ajustes, así que los dos van
      // siempre acompasados: nunca se puede armar la URL del logo de un inquilino con la
      // versión de otro.
      logoUrl: urlDelLogo(this.config.getOne('currentTenant')?.id, this.oNulo(ajustes[CLAVE_VERSION])),
    })),
  );

  /**
   * El nombre efectivo AHORA MISMO, sin observable.
   *
   * Lo necesita `LOGO_APP_NAME_TOKEN`, que el tema inyecta como un valor suelto y no como
   * algo a lo que suscribirse. Se puede leer así porque para cuando alguien inyecta ese
   * token la configuración de ABP ya está cargada: la trae un inicializador de la propia
   * aplicación, que corre antes de que se pinte nada.
   */
  nombreActual(): string {
    const propio = this.oNulo(this.config.getSetting(CLAVE_NOMBRE));
    return propio ?? this.textos.instant('::AppName');
  }

  obtener(): Observable<BrandingDto> {
    return this.rest.request<void, BrandingDto>(
      { method: 'GET', url: this.baseUrl },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  guardar(input: UpdateBrandingDto): Observable<BrandingDto> {
    return this.rest.request<UpdateBrandingDto, BrandingDto>(
      { method: 'PUT', url: this.baseUrl, body: input },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  subirLogo(imagen: Blob, nombre: string): Observable<BrandingDto> {
    const formulario = new FormData();
    // 'file' TIENE que coincidir con el nombre del parámetro de UploadLogoAsync: así es
    // como el model binder de ABP empareja el campo del formulario con el argumento.
    formulario.append('file', imagen, nombre);

    // 'upload-logo' y no 'logo': ABP deriva la ruta del NOMBRE del método
    // (UploadLogoAsync), y "Upload" no es un prefijo que reconozca, así que lo conserva
    // entero. Confirmado en Swagger, que es la única forma de saberlo — el borrado, en
    // cambio, sí se convierte en DELETE /logo. Ver CLAUDE.md §6.2.
    return this.rest.request<FormData, BrandingDto>(
      { method: 'POST', url: `${this.baseUrl}/upload-logo`, body: formulario },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  borrarLogo(): Observable<BrandingDto> {
    return this.rest.request<void, BrandingDto>(
      { method: 'DELETE', url: `${this.baseUrl}/logo` },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  /**
   * Recarga la configuración de ABP. Se llama tras guardar para que la barra lateral y la
   * pestaña se actualicen SIN recargar la página: los ajustes viajan dentro de esa
   * respuesta, y sin volver a pedirla el cambio no se vería hasta el siguiente arranque.
   */
  refrescarConfiguracion(): Observable<unknown> {
    return this.config.refreshAppState();
  }

  private oNulo(valor: string | undefined): string | null {
    return valor ? valor : null;
  }
}
