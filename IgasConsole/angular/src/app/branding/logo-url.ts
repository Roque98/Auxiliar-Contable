import { environment } from '../../environments/environment';

/**
 * Arma la URL pública del logo de un inquilino.
 *
 * ⚠️ **Esto está escrito DOS veces**, aquí y en `LectorDeMarca.ConstruirUrlDelLogo` del
 * servidor. No es un descuido: la pantalla de login es HTML que genera el servidor y no
 * ejecuta nada de este proyecto, así que allí la URL tiene que salir de C#; y aquí no se
 * puede pedir al servidor porque `ITenantBrandingAppService` exige el permiso
 * `IgasConsole.Branding` y el logo lo tiene que ver **todo el mundo**, también quien no puede
 * administrarlo.
 *
 * Lo que evita que las dos se separen es un test de integración que fija la forma exacta.
 *
 * El id del inquilino va SIEMPRE en la consulta, aunque la sesión ya lo diga: la respuesta
 * se cachea de forma indefinida (la versión distingue una imagen de otra), y si la URL
 * fuera la misma para todos, una caché por el camino podría servirle a un inquilino el logo
 * de otro.
 */
export function urlDelLogo(tenantId: string | null | undefined, version: string | null | undefined): string | null {
  if (!version) {
    return null;
  }

  const base = environment.apis.default.url;
  const v = encodeURIComponent(version);

  // Sin __tenant significa "el host", que también es una dirección distinta de las demás.
  return tenantId
    ? `${base}/api/branding/logo?__tenant=${encodeURIComponent(tenantId)}&v=${v}`
    : `${base}/api/branding/logo?v=${v}`;
}
