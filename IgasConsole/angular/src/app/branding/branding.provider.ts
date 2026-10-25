import { ReplaceableComponentsService } from '@abp/ng.core';
import { LOGO_APP_NAME_TOKEN, LOGO_URL_TOKEN } from '@abp/ng.theme.shared';
import { inject, provideAppInitializer } from '@angular/core';
import { Router, TitleStrategy } from '@angular/router';
import { BrandingService, MarcaActual } from './branding.service';
import { acentoParaTexto, relleno } from './contraste';
import { MarcaComponent } from './marca.component';

/**
 * `eThemeLeptonXComponents.Logo`, escrito a mano.
 *
 * No se importa el enum porque es un `declare const enum` de un `.d.ts`, y Angular compila
 * con `isolatedModules`: importarlo no compila. Es la misma trampa que ya obligó a escribir
 * `'blob' as any` en las descargas (ver CLAUDE.md §6.2).
 *
 * El valor está fijado en el layout del tema (`SideMenuApplicationLayoutComponent`), que
 * hace `*abpIfReplaceableTemplateExists` sobre esta misma clave: si dejara de existir, el
 * único síntoma sería que vuelve a verse la marca de serie.
 */
const CLAVE_LOGO_DEL_TEMA = 'Theme.LogoComponent';

/**
 * Aplica la marca del inquilino en el SPA.
 *
 * Son TRES enganches, y hacen falta los tres porque el tema pinta la marca de tres formas
 * distintas:
 *
 *  1. **El color y el logo son variables CSS** (`--lpx-brand`, `--lpx-logo`). Se redefinen
 *     en el elemento raíz, que gana a la hoja del tema sin `!important`, y las usan también
 *     los rincones donde el tema pinta `.lpx-brand-logo` por su cuenta.
 *  2. **El nombre no es CSS**: LeptonX lo inyecta de `LOGO_APP_NAME_TOKEN`, que de serie es
 *     un valor fijo tomado de `environment`. Se sustituye por una fábrica que lo lee de la
 *     configuración ya cargada.
 *  3. **`MarcaComponent`**, registrado en el punto de extensión `Theme.LogoComponent`, que
 *     es lo único reactivo: repinta la barra lateral al guardar, sin recargar la página.
 *
 * ⚠️ **Por qué 2 y 3 a la vez, si parecen lo mismo.** El componente sustituto NO entra en el
 * primer render. `IfReplaceableTemplateExistsDirective` crea el `ng-template` del logo en su
 * `ngAfterViewInit`, y para entonces `lpx-navbar` ya resolvió su consulta de contenido y se
 * quedó con la marca de serie; solo a partir de la primera navegación del SPA aparece el
 * componente propio. Comprobado en el navegador: recién entrado no hay `app-marca` y sí
 * `lpx-brand-logo`; tras navegar a cualquier pantalla, al revés. Con solo el punto (3), un
 * usuario vería el nombre de la plantilla justo después de entrar, que es el peor momento
 * posible. Con solo el (2), el nombre no cambiaría al guardar hasta recargar.
 *
 * Los dos caminos pintan lo mismo porque leen la misma fuente, así que el relevo no se nota.
 */
export const BRANDING_PROVIDER = [
  // El nombre para el componente de marca del propio tema — el que se ve hasta la primera
  // navegación. Es una FÁBRICA y no un valor: en el momento de construir los proveedores la
  // configuración de ABP todavía no está cargada, y cuando alguien inyecta el token sí.
  {
    provide: LOGO_APP_NAME_TOKEN,
    useFactory: () => inject(BrandingService).nombreActual(),
  },
  // Se deja VACÍO a propósito. Con una URL aquí, `BrandLogoComponent` toma la rama que pinta
  // solo el logo y deja de pintar el nombre; sin ella toma la que pinta los dos, y el logo
  // entra igual por la variable CSS del punto (1).
  { provide: LOGO_URL_TOKEN, useValue: '' },

  provideAppInitializer(() => {
    inject(ReplaceableComponentsService).add({
      key: CLAVE_LOGO_DEL_TEMA,
      component: MarcaComponent,
    });

    const marca = inject(BrandingService);
    const titulos = inject(TitleStrategy);
    const router = inject(Router);

    // Una sola suscripción para toda la vida de la aplicación: `marca$` deriva del estado
    // de configuración de ABP, que ya vuelve a emitir al entrar, al salir y al cambiar de
    // inquilino. No hace falta escuchar el login aparte.
    marca.marca$.subscribe(m => aplicar(m, titulos, router));
  }),
];

function aplicar(marca: MarcaActual, titulos: TitleStrategy, router: Router): void {
  const raiz = document.documentElement;

  // El acento del design system (`--ig-acento`) y el texto que va ENCIMA de él. Con el
  // color de serie no se toca nada: los tokens ya traen el amarillo y la tinta. Si el
  // Owner elige otro, `relleno` lo oscurece lo justo para que el texto llegue a AA.
  const relleno_ = relleno(marca.accentColor);
  fijar(raiz, '--ig-acento', relleno_?.fondo ?? null);
  fijar(raiz, '--ig-sobre-acento', relleno_?.texto ?? null);
  // Solo para el tema claro: el oscuro tiene su propio tono (ver `--ig-acento-texto` en los tokens).
  fijar(raiz, '--ig-acento-texto-marca', acentoParaTexto(marca.accentColor));
  fijar(raiz, '--bs-primary-rgb', aRgb(relleno_?.fondo ?? null));

  const url = marca.logoUrl ? `url(${marca.logoUrl})` : null;
  fijar(raiz, '--lpx-logo', url);
  fijar(raiz, '--lpx-logo-url', url);
  fijar(raiz, '--lpx-logo-icon', url);

  // La pestaña del navegador, para que el cambio se vea sin recargar ni navegar.
  //
  // Se hace re-lanzando la MISMA estrategia de títulos que corre en cada navegación
  // (`TituloConMarca`) en vez de asignar `document.title` a secas: así la pestaña queda
  // exactamente igual que si el usuario acabara de navegar aquí —con el nombre de la
  // pantalla delante— en lugar de perderlo hasta la siguiente navegación.
  titulos.updateTitle(router.routerState.snapshot);
}

/**
 * `#rrggbb` como terna `"r, g, b"`, que es el formato que Bootstrap espera en
 * `--bs-primary-rgb`. Devuelve null si el color no es válido, y entonces la variable se
 * quita en vez de quedarse a medias.
 *
 * Su gemelo en el servidor es `ColorDeMarca.ARgb`.
 */
function aRgb(color: string | null): string | null {
  if (!color || !/^#[0-9a-fA-F]{6}$/.test(color)) {
    return null;
  }
  const n = parseInt(color.slice(1), 16);
  return `${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}`;
}

/**
 * Pone la variable, o la QUITA cuando no hay valor.
 *
 * Quitarla importa: borrar el logo tiene que devolver el de serie, y una variable puesta a
 * la cadena vacía no hace eso — deja `background-image: url()`, que es un hueco. Sin valor,
 * la propiedad personalizada desaparece y vuelve a mandar la regla de `styles.scss`.
 */
function fijar(elemento: HTMLElement, propiedad: string, valor: string | null): void {
  if (valor) {
    elemento.style.setProperty(propiedad, valor);
  } else {
    elemento.style.removeProperty(propiedad);
  }
}
