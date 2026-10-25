import { ConfigStateService } from '@abp/ng.core';
import { inject, provideAppInitializer } from '@angular/core';
import { UserProfileService } from '@volo/ngx-lepton-x.core';
import { combineLatest } from 'rxjs';
import { ProfilePictureService } from './profile-picture.service';

/** El icono que pone LeptonX cuando no hay foto. Se replica para poder volver a él. */
const ICONO_POR_DEFECTO = 'bi bi-person-circle';

export const PROFILE_PICTURE_AVATAR_PROVIDER = [
  provideAppInitializer(() => {
    configurarAvatar();
  }),
];

/**
 * Hace que la foto de perfil se vea en el avatar de la barra superior.
 *
 * LeptonX arma su propio objeto de usuario a partir del estado de ABP y ahí fija el avatar
 * a un icono fijo (`bi bi-person-circle`); ABP open source no tiene foto de perfil, así
 * que no hay nada que pueda rellenar. Lo que sí expone es `patchUser`, y eso es lo que se
 * usa aquí: se deja que LeptonX construya el usuario como quiera y después se le cambia
 * solo el avatar.
 */
function configurarAvatar(): void {
  const fotos = inject(ProfilePictureService);
  const perfilDelTema = inject(UserProfileService);
  const config = inject(ConfigStateService);

  // La foto se pide al iniciar sesión y se olvida al cerrarla. Sin lo segundo, el
  // siguiente que entre en este navegador vería el avatar del anterior hasta recargar.
  config.getOne$('currentUser').subscribe(usuario => {
    if (usuario?.isAuthenticated) {
      fotos.cargar().subscribe({
        // Silencioso a propósito: que falle la foto no debe romper la barra superior ni
        // llenar la pantalla de errores. La página de foto de perfil sí los muestra.
        error: () => undefined,
      });
    } else {
      fotos.limpiar();
    }
  });

  combineLatest([fotos.foto$, perfilDelTema.user$]).subscribe(([foto, usuario]) => {
    const fuenteDeseada = foto ?? ICONO_POR_DEFECTO;

    // LA GUARDA IMPORTA: patchUser hace emitir a user$, que vuelve a entrar aquí. Sin
    // comparar antes, esto sería un bucle infinito.
    if (usuario?.avatar?.source === fuenteDeseada) {
      return;
    }

    perfilDelTema.patchUser({
      avatar: foto
        ? { type: 'image', source: foto }
        : { type: 'icon', source: ICONO_POR_DEFECTO },
    });
  });
}
