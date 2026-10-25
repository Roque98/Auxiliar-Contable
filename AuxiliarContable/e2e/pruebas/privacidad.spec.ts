import { expect, test } from '@playwright/test';

/*
 * El aviso de privacidad se lee ANTES de tener cuenta: el registro enlaza a él y tiene que
 * abrirse sin sesión. Que la API exija aceptarlo lo cubren los tests de integración.
 */
test('el aviso de privacidad se abre sin sesión', async ({ browser }) => {
  const contexto = await browser.newContext({ ignoreHTTPSErrors: true });
  const pagina = await contexto.newPage();
  await pagina.goto('/privacidad');

  await expect(pagina.getByRole('heading', { level: 1, name: 'Aviso de privacidad' })).toBeVisible({ timeout: 30_000 });
  await expect(pagina.getByRole('heading', { name: 'Tus derechos (ARCO) y cómo revocar tu consentimiento' })).toBeVisible();
  await expect(pagina.getByText(/Versión vigente: \d{4}-\d{2}-\d{2}\./)).toBeVisible();
  // Sin redirigir al login.
  expect(new URL(pagina.url()).pathname).toBe('/privacidad');
  await contexto.close();
});
