import { ErrorHandler, Injectable, Injector, Provider } from '@angular/core';
import { NavigationError } from '@angular/router';

/**
 * ⚠️⚠️ Recupera la pestaña que estaba abierta cuando se desplegó una versión nueva.
 *
 * <p><b>El problema, medido en producción.</b> Las pantallas se cargan de forma perezosa
 * (`loadComponent`/`loadChildren`), así que el `main.js` que un navegador ya tiene cargado
 * guarda los nombres de los trozos con el <b>hash de esa versión</b>. Al desplegar, esos
 * archivos desaparecen del servidor. Quien tuviera la aplicación abierta y navegue a una
 * pantalla que aún no había visitado pide un archivo que ya no existe y recibe un
 * <b>404</b>: la ruta no se monta y la pantalla no aparece. Pasó de verdad —«al ir a
 * usuarios y roles me da 404»— y no es un fallo del servidor: `/identity/users` responde 200
 * y todos los trozos de la versión desplegada están en su sitio.</p>
 *
 * <p>La caché ya está bien configurada y no es la causa: nginx sirve `index.html` con
 * `no-store` y los bundles con hash como `immutable`. Un refresco lo arregla — el asunto es
 * que <b>nadie sabe que tiene que refrescar</b>: lo que se ve es una pantalla que no
 * responde.</p>
 *
 * <p><b>Por qué recargar y no algo más fino.</b> Aquí no hay nada que salvar: el usuario
 * pidió una pantalla que todavía no se ha pintado, así que no hay formulario a medias que
 * perder. Recargar trae el `index.html` nuevo —que no se cachea— y con él los trozos
 * correctos, y la navegación continúa donde iba.</p>
 *
 * <p>⚠️ <b>Recarga UNA sola vez.</b> Si tras recargar el trozo sigue sin cargar —una red
 * caída, un despliegue a medias, un `dist` incompleto— repetir dejaría al navegador en un
 * bucle de recargas del que no se sale ni cerrando la pestaña. La marca vive en
 * `sessionStorage`: dura lo que la pestaña y no contamina otras.</p>
 *
 * <p>⚠️⚠️ <b>VERIFICADO A MEDIAS, y conviene saberlo antes de confiar en esto.</b> La
 * detección (`EsFalloDeCarga`) tiene tests con los mensajes reales de los dos constructores.
 * La <b>recuperación de punta a punta no se ha podido reproducir en local</b>: simular un
 * despliegue exige que la aplicación esté ya montada y que la navegación sea del router, y
 * cada intento se rompió por el arnés y no por el código — `page.goto` es una carga completa
 * de página (el 404 ocurre al arrancar, así que no hay navegación que falle), el menú de
 * LeptonX no expone `href` sobre el que pulsar, y las tarjetas de la portada no se pintan sin
 * datos. <b>La prueba fiel es la real</b>: dejar una pestaña abierta, desplegar, y navegar a
 * una pantalla no visitada. Hasta que eso se haga, esto es una red razonada, no comprobada.</p>
 *
 * <p>⚠️ Y se delega SIEMPRE en el manejador de Angular antes de nada. Un manejador de
 * errores que se traga lo que no entiende convierte cualquier excepción de la aplicación en
 * silencio, que es mucho peor que el problema que viene a resolver.</p>
 */
@Injectable()
export class RecargaTrasDespliegue implements ErrorHandler {
  /** Clave de la marca anti-bucle. Vive en la pestaña, no en el navegador entero. */
  static readonly MarcaDeRecarga = 'app:recarga-por-chunk';

  private readonly siguiente = new ErrorHandler();

  constructor(private readonly injector: Injector) {}

  handleError(error: unknown): void {
    // Primero lo de siempre: el error tiene que llegar a la consola pase lo que pase.
    this.siguiente.handleError(error);
    RecargarSiEsFalloDeCarga(error);
    // Y a la API, para verlo en OpenObserve (docs/observabilidad.md). Import diferido: este
    // archivo lo usan también las pruebas sin Angular completo.
    void import('./reporte-de-errores').then(m => this.injector.get(m.ReporteDeErrores).reportar(error)).catch(() => undefined);
  }
}

/**
 * ⚠️⚠️ <b>El Router NO deja llegar esto a `ErrorHandler`.</b> Atrapa el fallo del
 * `import()` de una ruta perezosa y lo emite como <c>NavigationError</c>, así que un
 * manejador de errores global —que fue el primer intento— <b>no se entera y no hace
 * nada</b>. Se vio simulando el despliegue: siete trozos servidos como 404, cero recargas.
 * Por eso el gancho de verdad es `withNavigationErrorHandler`, y el `ErrorHandler` se
 * conserva solo para los `import()` que no vienen del router.
 */
export function alFallarLaNavegacion(evento: NavigationError): void {
  RecargarSiEsFalloDeCarga(evento.error);
}

function RecargarSiEsFalloDeCarga(error: unknown): void {
  if (!EsFalloDeCarga(error) || YaRecargamos()) {
    return;
  }

  try {
    sessionStorage.setItem(RecargaTrasDespliegue.MarcaDeRecarga, '1');
  } catch {
    // Una pestaña privada o con el almacenamiento bloqueado puede lanzar aquí. Sin marca no
    // hay freno anti-bucle, así que en ese caso se prefiere NO recargar: una pantalla que no
    // carga se arregla con F5; un bucle de recargas, no.
    return;
  }

  location.reload();
}

function YaRecargamos(): boolean {
  try {
    return sessionStorage.getItem(RecargaTrasDespliegue.MarcaDeRecarga) === '1';
  } catch {
    return true;
  }
}

/**
 * ⚠️ Se reconoce por el NOMBRE y por el texto, no solo por `ChunkLoadError`.
 *
 * <p>Ese nombre lo pone webpack; el `application builder` de Angular usa `import()` nativo y
 * lo que llega es un `TypeError: Failed to fetch dynamically imported module: …`. Mirar solo
 * el nombre habría dejado el arreglo sin efecto justo en el constructor que usa este
 * proyecto — y sin que nada fallara, que es la peor forma de equivocarse.</p>
 */
export function EsFalloDeCarga(error: unknown): boolean {
  const e = error as { name?: string; message?: string } | null;
  if (!e) {
    return false;
  }

  const texto = `${e.name ?? ''} ${e.message ?? ''}`;

  return (
    /ChunkLoadError/i.test(texto) ||
    /Failed to fetch dynamically imported module/i.test(texto) ||
    /error loading dynamically imported module/i.test(texto) ||
    /Importing a module script failed/i.test(texto)
  );
}

export const RECARGA_TRAS_DESPLIEGUE_PROVIDER: Provider = {
  provide: ErrorHandler,
  useClass: RecargaTrasDespliegue,
};
