// Capturas y videos del manual de usuario, por rol, contra la consola LOCAL con los datos de
// ejemplo de sembrar.mjs. Deja todo en docs/manuales/ (img/<rol>/*.png y video/<rol>.webm).
//   node manual/sembrar.mjs && node manual/capturar.mjs
import { chromium } from '@playwright/test';
import { mkdirSync, renameSync, rmSync, existsSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { CUENTAS, CLIENTES, WEB, API } from './comun.mjs';

if (!/localhost|127\.0\.0\.1/.test(API)) {
  throw new Error(`Solo contra la consola local (API = ${API}).`);
}

const AQUI = dirname(fileURLToPath(import.meta.url));
const DOCS = resolve(AQUI, '..', '..', '..', 'docs', 'manuales');
const TAM = { width: 1280, height: 760 };
const SOLO = process.argv[2]; // opcional: un rol

/**
 * Qué se captura de cada rol. `cliente` elige al cliente en la pantalla; `accion` hace algo antes de la foto
 * (conciliar, abrir una pestaña). El chat está apagado: la portada es «Inicio».
 */
const ESCENAS = {
  usuario: [
    { ruta: '/', archivo: '01-inicio' },
    { ruta: '/documentos', archivo: '02-documentos' },
    { ruta: '/perfil-fiscal', archivo: '03-perfil-fiscal' },
    { ruta: '/leyes?q=deducción de automóviles', archivo: '04-consultar-leyes', accion: esperarLeyes },
    { ruta: '/mi-organizacion', archivo: '05-mi-organizacion' },
    { ruta: '/mi-cuenta', archivo: '06-mi-cuenta' },
    { ruta: '/seguridad', archivo: '07-seguridad' },
    { ruta: '/ayuda', archivo: '08-ayuda' },
  ],
  administrador: [
    { ruta: '/', archivo: '01-inicio' },
    { ruta: '/cartera', archivo: '02-cartera' },
    { ruta: '/conciliacion', archivo: '03-conciliacion', accion: conciliar },
    { ruta: null, archivo: '04-conciliacion-sin-factura', accion: p => partidas(p) },
    { ruta: '/documentos', archivo: '05-documentos-cliente', cliente: 'Panadería La Espiga' },
    { ruta: '/perfil-fiscal', archivo: '06-perfil-fiscal-cliente', cliente: 'Panadería La Espiga' },
    { ruta: '/leyes?q=artículo 27&ley=LISR', archivo: '07-consultar-leyes', accion: esperarLeyes },
    { ruta: '/organizaciones', archivo: '08-organizaciones' },
    { ruta: '/invitaciones', archivo: '09-invitaciones' },
    { ruta: '/identity/users', archivo: '10-usuarios' },
    { ruta: '/actividad', archivo: '11-actividad' },
  ],
  owner: [
    { ruta: '/', archivo: '01-inicio' },
    { ruta: '/cartera', archivo: '02-cartera-y-metricas' },
    { ruta: '/identity/users', archivo: '03-usuarios' },
    { ruta: '/identity/roles', archivo: '04-roles' },
    { ruta: '/invitaciones', archivo: '05-invitaciones' },
    { ruta: '/branding', archivo: '06-marca' },
    { ruta: '/actividad', archivo: '07-actividad' },
  ],
  developer: [
    { ruta: '/system-status', archivo: '01-estado-del-sistema' },
    { ruta: '/metrics', archivo: '02-metricas' },
    { ruta: '/jobs', archivo: '03-trabajos' },
    { ruta: '/audit', archivo: '04-auditoria' },
    { ruta: '/security', archivo: '05-seguridad-ips' },
    { ruta: '/api-keys', archivo: '06-llaves-api' },
    { ruta: '/alertas', archivo: '07-alertas' },
    { ruta: '/sessions', archivo: '08-sesiones' },
  ],
};

/** El mes de los ejemplos: octubre, aunque se suben en noviembre (ver ejemplos/generar.py). */
const MES_DE_EJEMPLO = { mes: 'octubre', anio: '2026' };

async function conciliar(pagina) {
  await pagina.locator('[data-testid=cliente]').selectOption({ label: 'Panadería La Espiga' });
  await pagina.locator('label:has-text("Mes") select').selectOption({ label: MES_DE_EJEMPLO.mes });
  await pagina.locator('label:has-text("Año") select').selectOption({ label: MES_DE_EJEMPLO.anio });
  await pagina.getByTestId('cierre').waitFor({ timeout: 30_000 });
  await pagina.getByTestId('conciliar').click();
  await pagina.getByTestId('partidas').waitFor({ timeout: 300_000 });
  await pagina.locator('.cc-resumen').waitFor({ timeout: 30_000 });
  await pagina.waitForTimeout(800);
}

/** Los movimientos sin factura del cierre, para decir qué es cada uno o preguntarle al cliente. */
async function partidas(pagina) {
  await pagina.getByTestId('partidas').scrollIntoViewIfNeeded();
  await pagina.waitForTimeout(600);
}

async function esperarLeyes(pagina) {
  await pagina.locator('.ly-resultados, .ly-vacio, .ig-alerta').first().waitFor({ timeout: 60_000 });
  await pagina.waitForTimeout(600);
}

async function ir(pagina, ruta) {
  await pagina.goto(`${WEB}${ruta}`);
  await pagina.locator('.ig-pagina, .ig-acceso-caja').first().waitFor({ timeout: 30_000 }).catch(() => undefined);
  await pagina.waitForLoadState('networkidle').catch(() => undefined);
  await pagina.waitForTimeout(900);
}

async function elegirCliente(pagina, nombre) {
  const select = pagina.locator('select').filter({ has: pagina.locator('option', { hasText: nombre }) }).first();
  if (await select.count()) {
    await select.selectOption({ label: nombre });
    await pagina.waitForLoadState('networkidle').catch(() => undefined);
    await pagina.waitForTimeout(900);
  }
}

async function aceptarAviso(pagina, foto) {
  const boton = pagina.getByRole('button', { name: 'Acepto el aviso de privacidad' });
  if (await boton.waitFor({ timeout: 6000 }).then(() => true).catch(() => false)) {
    if (foto) {
      await pagina.screenshot({ path: foto });
    }
    await boton.click();
    await boton.waitFor({ state: 'detached', timeout: 15_000 }).catch(() => undefined);
  }
}

async function entrar(navegador, c, video) {
  const contexto = await navegador.newContext({
    ignoreHTTPSErrors: true, locale: 'es-MX', viewport: TAM,
    recordVideo: video ? { dir: video, size: TAM } : undefined,
  });
  const pagina = await contexto.newPage();
  await pagina.goto(`${WEB}/account/login`);
  await pagina.waitForURL(/\/Account\/Login/i, { timeout: 60_000 });
  await pagina.waitForTimeout(600);
  return { contexto, pagina };
}

/** Quita las pistas "Default username/password" que pone dev-login-helper.js (solo en desarrollo). */
async function limpiarPistas(pagina) {
  await pagina.waitForTimeout(300);
  await pagina.evaluate(() => document.querySelectorAll('span.text-muted.small').forEach(s => {
    if (/^Default (username|password)/.test(s.textContent ?? '')) {
      s.remove();
    }
  }));
}

async function escribirLogin(pagina, c, lento, intento = 1) {
  const usuario = pagina.locator('#LoginInput_UserNameOrEmailAddress');
  const clave = pagina.locator('#LoginInput_Password');
  // dev-login-helper.js rellena admin/1q2w3E* al primer foco: enfocar antes, luego vaciar y escribir.
  await usuario.focus();
  await clave.focus();
  await pagina.waitForTimeout(250);
  await usuario.fill('');
  await clave.fill('');
  await limpiarPistas(pagina);
  await usuario.pressSequentially(c.usuario, { delay: lento ? 70 : 0 });
  await clave.pressSequentially(c.clave, { delay: lento ? 40 : 0 });
  await pagina.click('button[name=Action]');
  const entro = await pagina.waitForURL(u => !/\/Account\//i.test(u.pathname), { timeout: 20_000 }).then(() => true).catch(() => false);
  if (!entro) {
    if (intento >= 3) {
      throw new Error(`No se pudo entrar como ${c.usuario}. ¿Corriste sembrar.mjs?`);
    }
    // Límite de intentos por minuto: esperar a que se abra la ventana y volver a intentar.
    console.log('  esperando el límite de intentos de entrar…');
    await pagina.waitForTimeout(65_000);
    await pagina.goto(`${WEB}/account/login`);
    await pagina.waitForURL(/\/Account\/Login/i, { timeout: 60_000 });
    return escribirLogin(pagina, c, lento, intento + 1);
  }
  await pagina.waitForTimeout(1500);
}

// --- Recorridos en video (más lentos, con acciones) ----------------------------------------
const RECORRIDOS = {
  usuario: async p => {
    await ir(p, '/');
    await p.waitForTimeout(1500);
    await ir(p, '/documentos');
    const entrada = p.locator('input[type=file]').first();
    if (await entrada.count()) {
      // Un ticket es «Ticket»: con el valor por omisión (Estado de cuenta) la conciliación intentaría leerlo.
      await p.locator('label:has-text("Si no es factura") select').selectOption({ label: 'Ticket' });
      await p.waitForTimeout(800);
      await entrada.setInputFiles({
        name: 'ticket-gasolina.pdf', mimeType: 'application/pdf',
        buffer: Buffer.from('%PDF-1.4\n% ticket de ejemplo\n%%EOF\n'),
      });
      await p.waitForTimeout(2500);
    }
    await ir(p, '/perfil-fiscal');
    await p.mouse.wheel(0, 600);
    await p.waitForTimeout(1500);
    await ir(p, '/leyes');
    await p.locator('[data-testid=consulta]').pressSequentially('deducción de automóviles', { delay: 60 });
    await p.getByRole('button', { name: 'Buscar', exact: true }).click();
    await esperarLeyes(p);
    await p.mouse.wheel(0, 500);
    await p.waitForTimeout(1500);
  },
  administrador: async p => {
    await ir(p, '/');
    await p.waitForTimeout(1500);
    await ir(p, '/cartera');
    await p.waitForTimeout(1500);
    await ir(p, '/conciliacion');
    await conciliar(p);
    await p.mouse.wheel(0, 600);
    await p.waitForTimeout(1500);
    await partidas(p);
    await p.waitForTimeout(1500);
    await ir(p, '/perfil-fiscal');
    await elegirCliente(p, 'Panadería La Espiga');
    await p.waitForTimeout(1500);
    await ir(p, '/leyes?q=artículo 27&ley=LISR');
    await esperarLeyes(p);
    await p.waitForTimeout(1500);
  },
  owner: async p => {
    await ir(p, '/');
    await p.waitForTimeout(1500);
    await ir(p, '/cartera');
    await p.waitForTimeout(2000);
    await ir(p, '/identity/users');
    await ir(p, '/identity/roles');
    await ir(p, '/branding');
    await p.waitForTimeout(1500);
  },
  developer: async p => {
    await p.waitForTimeout(1500);
    await ir(p, '/system-status');
    await p.waitForTimeout(1500);
    await ir(p, '/metrics');
    await ir(p, '/jobs');
    await ir(p, '/audit');
    await p.waitForTimeout(1500);
  },
};

const navegador = await chromium.launch({ args: ['--ignore-certificate-errors'] });
mkdirSync(join(DOCS, 'img', 'comun'), { recursive: true });
mkdirSync(join(DOCS, 'video'), { recursive: true });

// Pantalla de entrar (sin sesión), una vez.
{
  const { contexto, pagina } = await entrar(navegador, CUENTAS.usuario);
  await limpiarPistas(pagina);
  await pagina.locator('#LoginInput_UserNameOrEmailAddress, #LoginInput_Password').evaluateAll(es => es.forEach(e => (e.value = '')));
  await pagina.screenshot({ path: join(DOCS, 'img', 'comun', '01-entrar.png') });
  await contexto.close();
}

for (const [rol, escenas] of Object.entries(ESCENAS)) {
  if (SOLO && SOLO !== rol) {
    continue;
  }
  const c = CUENTAS[rol];
  const carpeta = join(DOCS, 'img', rol);
  mkdirSync(carpeta, { recursive: true });

  // Una sola sesión por rol: el video graba el recorrido y las capturas salen del mismo recorrido
  // (el login tiene límite de intentos por minuto).
  const tmp = join(DOCS, 'video', `_${rol}`);
  rmSync(tmp, { recursive: true, force: true });
  const { contexto, pagina } = await entrar(navegador, c, tmp);
  await escribirLogin(pagina, c, true);
  await aceptarAviso(pagina, rol === 'usuario' ? join(DOCS, 'img', 'comun', '02-aviso-de-privacidad.png') : null);
  await pagina.waitForTimeout(1200);
  for (const e of escenas) {
    if (e.ruta) {
      await ir(pagina, e.ruta);
    }
    if (e.cliente) {
      await elegirCliente(pagina, e.cliente);
    }
    if (e.accion) {
      await e.accion(pagina);
    }
    await pagina.screenshot({ path: join(carpeta, `${e.archivo}.png`) });
    console.log(rol, e.archivo);
    await pagina.waitForTimeout(700);
  }
  await RECORRIDOS[rol](pagina);
  const video = pagina.video();
  await contexto.close();
  const destino = join(DOCS, 'video', `${rol}.webm`);
  rmSync(destino, { force: true });
  renameSync(await video.path(), destino);
  rmSync(tmp, { recursive: true, force: true });
  console.log(rol, 'video listo');
}
await navegador.close();
console.log('listo:', DOCS, existsSync(DOCS));
