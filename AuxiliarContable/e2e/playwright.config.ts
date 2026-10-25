import { defineConfig } from '@playwright/test';

/**
 * Pruebas de pantalla contra la consola LOCAL (API en https://localhost:44338 y Angular en
 * http://localhost:4201, ver README.md). No arrancan nada solas: el backend necesita la
 * base migrada y sus user-secrets, y eso no se puede adivinar desde aquí.
 *
 * ⚠️ Un solo worker y en serie. El login rota el refresh token: dos navegadores con la misma
 * sesión guardada reusan un token ya canjeado y OpenIddict revoca la sesión entera (pasó).
 * Además las rutas de login tienen límite de peticiones por IP.
 */
export default defineConfig({
  testDir: './pruebas',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.AUXILIAR_E2E_URL ?? 'http://localhost:4201',
    ignoreHTTPSErrors: true,
    locale: 'es-MX',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
