import { expect, test } from '@playwright/test';
import { CLAVE, USUARIO } from './ayudas';

/*
 * Lo que rompió producción: un doble toque en «Entrar» mandaba dos POST; el segundo caía en un
 * error 400 (antifalsificación) y desde ahí se acababa en la portada de la API (la de la
 * plantilla de ABP) en vez de en la consola.
 */
const API = process.env.IGAS_E2E_API ?? 'https://localhost:44338';

test('la raíz de la API lleva a la consola', async ({ browser }) => {
  const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
  const pagina = await contexto.newPage();
  const respuesta = await pagina.request.get(API + '/', { maxRedirects: 0 });
  expect(respuesta.status()).toBe(302);
  expect(respuesta.headers()['location']).toMatch(/^https?:\/\/[^/]+\/$/);
  await contexto.close();
});

test('un doble clic en «Entrar» deja dentro de la consola, no en un error', async ({ browser }) => {
  const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
  const pagina = await contexto.newPage();
  await pagina.goto('/account/login');
  await pagina.waitForURL(/\/Account\/Login/i, { timeout: 30_000 });
  await pagina.locator('#LoginInput_UserNameOrEmailAddress').evaluate((el: HTMLInputElement, v) => (el.value = v), USUARIO);
  await pagina.locator('#LoginInput_Password').evaluate((el: HTMLInputElement, v) => (el.value = v), CLAVE);
  await pagina.locator('button[name=Action]').dblclick();
  await pagina.waitForURL(u => !/\/(Account|Error)\b/i.test(u.pathname), { timeout: 60_000 });
  await expect(pagina.locator('button[aria-label^="Cambiar a modo"]')).toBeVisible({ timeout: 60_000 });
  await contexto.close();
});

test('reenviar el login con la sesión ya iniciada lleva a la consola (antes: error 400)', async ({ browser }) => {
  // Lo que pasó en producción, sin depender de la latencia: el formulario de la pestaña A se
  // cargó sin sesión; la pestaña B inicia sesión; A envía su formulario viejo.
  const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
  const a = await contexto.newPage();
  await a.goto('/account/login');
  await a.waitForURL(/\/Account\/Login/i, { timeout: 30_000 });
  const b = await contexto.newPage();
  await b.goto('/account/login');
  await b.waitForURL(/\/Account\/Login/i, { timeout: 30_000 });
  for (const p of [b, a]) {
    await p.locator('#LoginInput_UserNameOrEmailAddress').evaluate((el: HTMLInputElement, v) => (el.value = v), USUARIO);
    await p.locator('#LoginInput_Password').evaluate((el: HTMLInputElement, v) => (el.value = v), CLAVE);
  }
  await b.locator('button[name=Action]').click();
  await b.waitForURL(u => !/\/Account\//i.test(u.pathname), { timeout: 60_000 });
  await a.locator('button[name=Action]').click();
  await a.waitForURL(u => !/\/(Account|Error)/i.test(u.pathname), { timeout: 60_000 });
  await expect(a.locator('button[aria-label^="Cambiar a modo"]')).toBeVisible({ timeout: 60_000 });
  await contexto.close();
});
