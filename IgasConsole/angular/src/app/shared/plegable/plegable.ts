/**
 * La LÓGICA de las secciones plegables, sin Angular ni DOM (se prueba en node, ver
 * vitest.config.mts). El componente (plegable.component.ts) solo la usa.
 */

/** Prefijo de las claves de localStorage: `zia.plegable.<clave de la sección>`. */
export const PREFIJO_PLEGABLE = 'zia.plegable.';

/** Lo que se usa de `localStorage`: así las pruebas pasan uno en memoria. */
export interface AlmacenPlegable {
  getItem(clave: string): string | null;
  setItem(clave: string, valor: string): void;
}

/** Lo guardado de una sección: `true` abierta, `false` cerrada, `null` nunca se tocó (o no hay almacén). */
export function leerPlegable(almacen: AlmacenPlegable | null, clave: string): boolean | null {
  try {
    const v = almacen?.getItem(PREFIJO_PLEGABLE + clave);
    return v === '1' ? true : v === '0' ? false : null;
  } catch {
    // Modo privado de Safari, cuota llena, almacenamiento bloqueado: se comporta como «nunca tocada».
    return null;
  }
}

export function guardarPlegable(almacen: AlmacenPlegable | null, clave: string, abierta: boolean): void {
  try {
    almacen?.setItem(PREFIJO_PLEGABLE + clave, abierta ? '1' : '0');
  } catch {
    // Sin almacenamiento, la sección funciona igual; solo no se recuerda.
  }
}

/**
 * Cómo arranca una sección. Una con errores o avisos se abre SIEMPRE, aunque el usuario la
 * cerrara la última vez: un problema escondido detrás de un clic es justo lo que no debe
 * pasar. Si no, lo último que eligió el usuario; si nunca la tocó, lo que diga la pantalla.
 */
export function abiertaAlEmpezar(o: { porDefecto: boolean; guardado: boolean | null; alerta: boolean }): boolean {
  if (o.alerta) {
    return true;
  }
  return o.guardado ?? o.porDefecto;
}

/** ¿Alguna de estas clases de estado (`exito`/`alerta`/`error`/`neutro`) pide atención? */
export function hayAtencion(clases: readonly string[]): boolean {
  return clases.some(c => c === 'error' || c === 'alerta');
}

/**
 * Navegación con teclado entre las cabeceras de un grupo (el patrón «acordeón» de WAI-ARIA):
 * flechas arriba/abajo (dando la vuelta), Inicio y Fin. Devuelve el índice al que mover el
 * foco, o `null` si la tecla no es de navegación (Enter y Espacio ya los resuelve el botón).
 */
export function siguienteCabecera(actual: number, total: number, tecla: string): number | null {
  if (total <= 0 || actual < 0) {
    return null;
  }
  switch (tecla) {
    case 'ArrowDown':
      return (actual + 1) % total;
    case 'ArrowUp':
      return (actual - 1 + total) % total;
    case 'Home':
      return 0;
    case 'End':
      return total - 1;
    default:
      return null;
  }
}
