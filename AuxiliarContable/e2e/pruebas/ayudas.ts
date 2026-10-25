import { Browser, BrowserContext, Page, expect } from '@playwright/test';

export const USUARIO = process.env.AUXILIAR_E2E_USUARIO ?? 'admin';
// La contraseña por defecto es la del admin de DESARROLLO que siembra ABP; en cualquier otro
// entorno se pasa por la variable de entorno.
export const CLAVE = process.env.AUXILIAR_E2E_CLAVE ?? '1q2w3E*';

/** Las pantallas que se recorren en las pruebas generales. */
export const RUTAS = [
  '/',
  '/organizaciones',
  '/actividad',
  '/mi-organizacion',
  '/identity/users',
  '/identity/roles',
  '/invitaciones',
  '/seguridad',
  '/ayuda',
  '/mi-cuenta',
  '/sessions',
  '/system-status',
  '/metrics',
  '/audit',
  '/security',
  '/jobs',
  '/telegram',
  '/alertas',
  '/branding',
  '/notifications',
  '/api-keys',
];

/**
 * Inicia sesión una vez. Si las rutas de login responden «demasiadas peticiones» (límite por
 * IP), espera a que se abra la ventana y lo intenta de nuevo.
 */
export async function contextoConSesion(browser: Browser): Promise<{ contexto: BrowserContext; pagina: Page }> {
  const contexto = await browser.newContext();
  const pagina = await contexto.newPage();

  for (let intento = 0; intento < 4; intento++) {
    await pagina.goto('/account/login');
    await pagina.waitForURL(/\/Account\/Login/i, { timeout: 30_000 }).catch(() => undefined);
    const campo = pagina.locator('#LoginInput_UserNameOrEmailAddress');
    // waitFor y no isVisible: isVisible NO espera (ignora el timeout) y contesta «no» si la
    // página aún no terminó de pintar.
    if (await campo.waitFor({ timeout: 15_000 }).then(() => true).catch(() => false)) {
      // evaluate y no fill: en desarrollo el formulario viene relleno y fill concatena.
      await campo.evaluate((el: HTMLInputElement, v) => (el.value = v), USUARIO);
      await pagina.locator('#LoginInput_Password').evaluate((el: HTMLInputElement, v) => (el.value = v), CLAVE);
      await pagina.click('button[name=Action]');
      await pagina.waitForURL(u => !/\/Account\//i.test(u.pathname), { timeout: 60_000 });
      await expect(pagina.locator('button[aria-label^="Cambiar a modo"]')).toBeVisible({ timeout: 60_000 });
      return { contexto, pagina };
    }
    await pagina.waitForTimeout(65_000);
  }
  throw new Error('No se pudo iniciar sesión (¿API caída o límite de peticiones?).');
}

/** Navega y espera a que la pantalla haya pintado algo más que el esqueleto. */
export async function ir(pagina: Page, ruta: string): Promise<void> {
  await pagina.goto(ruta);
  // Las páginas públicas (/registro) no llevan `.ig-pagina`: van en la caja de acceso.
  await pagina.locator('.ig-pagina, .ig-acceso-caja').first().waitFor({ timeout: 30_000 });
  await pagina.waitForLoadState('networkidle').catch(() => undefined);
  await pagina.waitForTimeout(500);
}

export async function ponerTema(pagina: Page, tema: 'claro' | 'oscuro'): Promise<void> {
  await pagina.evaluate(t => localStorage.setItem('ig-tema', t), tema);
}

