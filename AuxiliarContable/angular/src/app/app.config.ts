import { provideAbpCore, withOptions, withTitleStrategy } from '@abp/ng.core';
import { provideAbpOAuth } from '@abp/ng.oauth';
import { provideSettingManagementConfig } from '@abp/ng.setting-management/config';
import { provideAbpThemeShared,} from '@abp/ng.theme.shared';
import { provideIdentityConfig } from '@abp/ng.identity/config';
import { provideAccountConfig } from '@abp/ng.account/config';
import { registerLocaleForEsBuild } from '@abp/ng.core/locale';
import { provideThemeLeptonX } from '@abp/ng.theme.lepton-x';
import { provideSideMenuLayout } from '@abp/ng.theme.lepton-x/layouts';
import { ApplicationConfig } from '@angular/core';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideRouter, withNavigationErrorHandler } from '@angular/router';
import { environment } from '../environments/environment';
import { APP_ROUTES } from './app.routes';
import { APP_ROUTE_PROVIDER } from './route.provider';
import { FOOTER_PROVIDER } from './footer/footer.config';
import { PROFILE_PICTURE_AVATAR_PROVIDER } from './profile-picture/avatar.provider';
import { LAYOUT_PROVIDER } from './layout/layout.provider';
import { BRANDING_PROVIDER } from './branding/branding.provider';
import { USUARIOS_PROVIDER } from './usuarios/usuarios.provider';
import { TituloConMarca } from './branding/titulo-con-marca';
import {
  RECARGA_TRAS_DESPLIEGUE_PROVIDER,
  alFallarLaNavegacion,
} from './shared/recarga-tras-despliegue';

export const appConfig: ApplicationConfig = {
  providers: [
    // ⚠️ `withNavigationErrorHandler` y no solo el `ErrorHandler` global: el Router atrapa
    // el fallo de carga de una ruta perezosa y lo emite como `NavigationError`, así que al
    // manejador de errores no le llega nada. Recupera la pestaña que estaba abierta cuando
    // se desplegó una versión nueva — sus trozos ya no existen y la pantalla daba 404. Ver
    // `recarga-tras-despliegue.ts`.
    provideRouter(APP_ROUTES, withNavigationErrorHandler(alFallarLaNavegacion)),
    RECARGA_TRAS_DESPLIEGUE_PROVIDER,
    APP_ROUTE_PROVIDER,
    FOOTER_PROVIDER,
    provideAnimations(),
    provideAbpCore(
      withOptions({
        environment,
        registerLocaleFn: registerLocaleForEsBuild(),
      }),
      // El título de la pestaña con el nombre configurado en la marca. Sin esto,
      // AbpTitleStrategy lo reescribe en cada navegación con el nombre fijo del proyecto.
      withTitleStrategy(TituloConMarca),
    ),
    provideAbpOAuth(),
    provideIdentityConfig(),
    provideSettingManagementConfig(),
    provideThemeLeptonX(),
    provideSideMenuLayout(),
    // DESPUÉS del tema: sustituye su layout de aplicación por el nuestro.
    LAYOUT_PROVIDER,
    // DESPUÉS de provideThemeLeptonX: parchea el avatar que ese tema acaba de fijar.
    PROFILE_PICTURE_AVATAR_PROVIDER,
    // También después: sustituye el componente de marca del tema y redefine sus variables
    // CSS. Antes, el tema se registraría encima y volvería a poner las suyas.
    BRANDING_PROVIDER,
    USUARIOS_PROVIDER,
    provideAccountConfig(),
    provideAbpThemeShared(),
  ]
};
