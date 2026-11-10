import { ConfigStateService } from '@abp/ng.core';
import { Signal, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';

/** El ajuste del servidor (AuxiliarContableSettings.AsistenteHabilitado). */
export const AJUSTE_DEL_CHAT = 'AuxiliarContable.Asistente.Habilitado';

/**
 * ¿Está encendido el chat con IA? Apagado (sin API de Claude), la consola se usa solo por
 * pantallas: ni el menú ni los botones de «Preguntar al asistente» lo enseñan. Llamar dentro de
 * un contexto de inyección (campo de un componente).
 */
export function chatEncendido(): Signal<boolean> {
  const config = inject(ConfigStateService);
  return toSignal(config.getSetting$(AJUSTE_DEL_CHAT).pipe(map(v => String(v).toLowerCase() === 'true')), {
    initialValue: String(config.getSetting(AJUSTE_DEL_CHAT)).toLowerCase() === 'true',
  });
}
