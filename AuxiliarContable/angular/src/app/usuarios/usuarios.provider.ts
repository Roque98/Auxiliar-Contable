import { inject, provideAppInitializer } from '@angular/core';
import { ReplaceableComponentsService } from '@abp/ng.core';
import { UsuariosComponent } from './usuarios.component';

/**
 * Sustituye la pantalla de Usuarios de `@abp/ng.identity` por la propia. La clave es la de
 * ABP (`eIdentityComponents.Users`, verificado en node_modules/@abp/ng.identity): ruta,
 * menú, guardas y permisos siguen siendo los suyos.
 */
export const USUARIOS_PROVIDER = [
  provideAppInitializer(() => {
    inject(ReplaceableComponentsService).add({
      key: 'Identity.UsersComponent',
      component: UsuariosComponent,
    });
  }),
];
