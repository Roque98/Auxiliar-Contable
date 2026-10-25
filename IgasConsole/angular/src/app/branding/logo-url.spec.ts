import { describe, expect, it } from 'vitest';
import { urlDelLogo } from './logo-url';
import { environment } from '../../environments/environment';

/**
 * La forma de la URL del logo, del lado del navegador.
 *
 * ⚠️ Igual que `ruta-de-inquilino.spec.ts`, esto existe porque **la misma URL se construye en
 * dos lenguajes**: aquí para el SPA, y en `LectorDeMarca.ConstruirUrlDelLogo` para la página
 * de login, que es HTML del servidor y no ejecuta nada de este proyecto. No es duplicación por
 * descuido: el SPA no puede pedírsela al servidor porque `ITenantBrandingAppService` exige el
 * permiso `IgasConsole.Branding` y el logo lo tiene que ver todo el mundo.
 *
 * Lo que vigilan estos casos no es el formato por el formato: es que **el inquilino vaya
 * siempre en la consulta**. La respuesta del logo se marca cacheable de forma indefinida, así
 * que si la URL fuera la misma para todos, una caché por el camino podría servirle a un
 * inquilino **el logo de otro**. Ese fallo no se ve en desarrollo —no hay caché intermedia— y
 * aparecería en producción como una marca ajena en la pantalla de login de un cliente.
 */
describe('la URL del logo', () => {
  const base = environment.apis.default.url;

  it('sin versión no hay logo', () => {
    // La versión es también la señal de «este inquilino tiene logo»: sin ella no se pide una
    // imagen que no existe.
    expect(urlDelLogo('11111111-1111-1111-1111-111111111111', null)).toBeNull();
    expect(urlDelLogo('11111111-1111-1111-1111-111111111111', undefined)).toBeNull();
    expect(urlDelLogo('11111111-1111-1111-1111-111111111111', '')).toBeNull();
  });

  it('⚠️ el inquilino va SIEMPRE en la consulta', () => {
    const url = urlDelLogo('11111111-1111-1111-1111-111111111111', 'a1b2c3d4');

    expect(url).toBe(
      `${base}/api/branding/logo?__tenant=11111111-1111-1111-1111-111111111111&v=a1b2c3d4`,
    );
    // Dicho aparte y sin depender del formato exacto: si esto se cae, una caché compartida
    // puede servir el logo de un inquilino a otro.
    expect(url).toContain('__tenant=');
  });

  it('sin inquilino es el host, que también es una URL distinta', () => {
    expect(urlDelLogo(null, 'a1b2c3d4')).toBe(`${base}/api/branding/logo?v=a1b2c3d4`);
    expect(urlDelLogo(undefined, 'a1b2c3d4')).toBe(`${base}/api/branding/logo?v=a1b2c3d4`);
  });

  it('escapa la versión', () => {
    // La versión la genera el servidor y es alfanumérica, pero escaparla es lo que impide
    // que un valor inesperado se salga de la consulta.
    expect(urlDelLogo(null, 'a b&c')).toBe(`${base}/api/branding/logo?v=a%20b%26c`);
  });

  it('el orden de los parámetros es __tenant y luego v, igual que en C#', () => {
    // No es un capricho: las dos mitades tienen que producir la MISMA cadena, porque la
    // respuesta se cachea por URL. Dos órdenes distintos son dos entradas de caché para la
    // misma imagen — y el `immutable` las haría durar.
    const url = urlDelLogo('22222222-2222-2222-2222-222222222222', 'ffffffff')!;

    expect(url.indexOf('__tenant=')).toBeLessThan(url.indexOf('&v='));
  });
});
