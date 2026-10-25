import { BrowserContext, Page, expect, test } from '@playwright/test';
import { RUTAS, contextoConSesion, ir, ponerTema } from './ayudas';

/*
 * Lo que se comprobó a mano mientras se construía la consola, ahora repetible. Todas
 * comparten UNA sesión (ver playwright.config.ts).
 */
test.describe.configure({ mode: 'serial' });

let contexto: BrowserContext;
let pagina: Page;

test.beforeAll(async ({ browser }) => {
  ({ contexto, pagina } = await contextoConSesion(browser));
});

test.afterAll(async () => {
  await contexto?.close();
});

test('ninguna pantalla se desborda ni esconde pestañas, en teléfono, tablet y escritorio', async () => {
  // Recorre todas las pantallas a cuatro anchos: no cabe en el tiempo por defecto.
  test.setTimeout(360_000);
  // 390: teléfono. 768: tablet vertical (menú desplegable). 1024: tablet horizontal (menú
  // fijo, el ancho más justo). 1280: escritorio.
  const problemas: string[] = [];
  for (const ancho of [390, 768, 1024, 1280]) {
    await pagina.setViewportSize({ width: ancho, height: 900 });
    for (const ruta of RUTAS) {
      await ir(pagina, ruta);
      const sobra = await pagina.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      if (sobra > 1) {
        problemas.push(`${ruta} a ${ancho}px: ${sobra}px de más`);
      }
      // Una barra de pestañas que desliza con la barra de desplazamiento oculta esconde
      // pestañas sin ninguna pista: tienen que caber (o pasar a otra fila).
      const escondidas = await pagina.evaluate(() =>
        Array.from(document.querySelectorAll('.ig-pagina .ig-pestanas'))
          .filter(b => (b as HTMLElement).offsetParent !== null && b.scrollWidth - b.clientWidth > 1)
          .map(b => b.getAttribute('aria-label') ?? 'pestañas'),
      );
      for (const e of escondidas) {
        problemas.push(`${ruta} a ${ancho}px: «${e}» esconde pestañas`);
      }
      // Un <select> mide lo que su opción más larga: se salía del panel (que lo recorta, así
      // que la página no «sobraba») y en el teléfono quedaba cortado por la derecha.
      const cortados = await pagina.evaluate(() =>
        Array.from(document.querySelectorAll<HTMLElement>('.ig-pagina .ig-control'))
          .filter(c => {
            if (c.offsetParent === null) {
              return false;
            }
            const panel = c.closest('.ig-panel') ?? document.documentElement;
            return c.getBoundingClientRect().right - panel.getBoundingClientRect().right > 1;
          })
          .map(c => c.getAttribute('aria-label') ?? c.getAttribute('name') ?? c.tagName),
      );
      for (const c of cortados) {
        problemas.push(`${ruta} a ${ancho}px: el control «${c}» se sale del panel`);
      }
    }
  }
  expect(problemas, problemas.join('\n')).toEqual([]);
});

test('en modo oscuro no quedan fondos blancos ni textos ilegibles', async () => {
  await pagina.setViewportSize({ width: 1280, height: 900 });
  await ponerTema(pagina, 'oscuro');
  const problemas: string[] = [];

  for (const ruta of RUTAS) {
    await ir(pagina, ruta);
    const hallazgos = await pagina.evaluate(() => {
      // `color-mix` sale como `color(srgb 0.38 0.55 0.77)`: en 0..1, no en 0..255. Leído como rgb()
      // daba casi negro y un contraste falso de 1,2.
      const rgb = (c: string) => {
        const n = (c.match(/[\d.]+/g) || []).map(Number);
        return c.startsWith('color(srgb') ? [...n.slice(0, 3).map(v => v * 255), ...n.slice(3)] : n;
      };
      const lum = ([r, g, b]: number[]) => {
        const f = (v: number) => ((v /= 255) <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
        return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
      };
      const opaco = (c: number[]) => c.length >= 3 && (c.length < 4 || c[3] > 0.5);
      const fondoDe = (el: Element | null): number[] => {
        for (let e = el; e; e = e.parentElement) {
          const c = rgb(getComputedStyle(e).backgroundColor);
          if (opaco(c)) return c;
        }
        return [22, 24, 27];
      };
      const salida = new Set<string>();
      for (const el of Array.from(document.querySelectorAll('.ig-pagina *'))) {
        if (el.closest('iframe, svg, .an-marco')) continue;
        const r = el.getBoundingClientRect();
        if (!r.width || !r.height) continue;
        const cs = getComputedStyle(el);
        if (cs.visibility === 'hidden' || cs.display === 'none' || +cs.opacity === 0) continue;
        const bg = rgb(cs.backgroundColor);
        // Un objeto físico (el ticket impreso) es claro a propósito: se marca y se perdona el fondo,
        // no el contraste de su texto.
        if (opaco(bg) && lum(bg) > 0.7 && r.width * r.height > 400 && !el.hasAttribute('data-claro-a-proposito')) {
          salida.add(`fondo claro en <${el.tagName.toLowerCase()} class="${el.className}">`);
        }
        const tieneTexto = Array.from(el.childNodes).some(n => n.nodeType === 3 && n.textContent!.trim());
        if (tieneTexto) {
          const a = lum(rgb(cs.color));
          const b = lum(fondoDe(el));
          const contraste = (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
          if (contraste < 3) {
            salida.add(`contraste ${contraste.toFixed(1)} en «${el.textContent!.trim().slice(0, 30)}»`);
          }
        }
      }
      return Array.from(salida).slice(0, 5);
    });
    problemas.push(...hallazgos.map(h => `${ruta}: ${h}`));
  }

  await ponerTema(pagina, 'claro');
  expect(problemas, problemas.join('\n')).toEqual([]);
});

test('el cambio de tema es un fundido, no un salto', async () => {
  await pagina.setViewportSize({ width: 390, height: 844 });
  await ponerTema(pagina, 'claro');
  await ir(pagina, '/');

  const brillo = async () => {
    const png = await pagina.screenshot({ clip: { x: 0, y: 300, width: 390, height: 200 } });
    // Muestra del píxel central de la captura, sin librerías: basta con el color de fondo.
    return pagina.evaluate(async datos => {
      const img = new Image();
      img.src = 'data:image/png;base64,' + datos;
      await img.decode();
      const c = document.createElement('canvas');
      c.width = img.width;
      c.height = img.height;
      const ctx = c.getContext('2d')!;
      ctx.drawImage(img, 0, 0);
      const d = ctx.getImageData(0, 0, c.width, c.height).data;
      let suma = 0;
      for (let i = 0; i < d.length; i += 4) suma += (d[i] + d[i + 1] + d[i + 2]) / 3;
      return suma / (d.length / 4);
    }, png.toString('base64'));
  };

  const antes = await brillo();
  await pagina.click('button[aria-label^="Cambiar a modo"]');
  await pagina.waitForTimeout(300);
  const aMedias = await brillo();
  await pagina.waitForTimeout(900);
  const despues = await brillo();

  expect(despues).toBeLessThan(antes - 100);
  // A los 300 ms tiene que estar ENTRE los dos: si ya está en el final, fue un salto.
  expect(aMedias).toBeLessThan(antes - 10);
  expect(aMedias).toBeGreaterThan(despues + 10);

  await pagina.click('button[aria-label^="Cambiar a modo"]');
  await pagina.waitForTimeout(1000);
});

test('Usuarios: al crear se elige un solo rol, y el Developer no se ofrece', async () => {
  await pagina.setViewportSize({ width: 390, height: 844 });
  await ir(pagina, '/identity/users');
  await pagina.click('button:has-text("Crear a mano")');
  await expect(pagina.locator('.us-rol').first()).toBeVisible();

  await expect(pagina.locator('.us-rol strong')).toHaveText(['Owner', 'Administrador', 'Usuario']);
  await expect(pagina.locator('.us-rol.elegido strong')).toHaveText(['Usuario']);

  await pagina.click('.us-rol:has-text("Owner")');
  await expect(pagina.locator('.us-rol.elegido strong')).toHaveText(['Owner']);

  await pagina.click('label:has-text("Aún no tiene correo")');
  await expect(pagina.locator('#us-correo')).toHaveCount(0);

  await pagina.click('.modal-footer button:has-text("Cancelar")');
});

test('Seguridad: correo, teléfono, dos pasos y contraseña', async () => {
  await ir(pagina, '/seguridad');
  for (const titulo of ['Correo', 'Teléfono', 'Verificación en dos pasos', 'Contraseña']) {
    await expect(pagina.getByRole('heading', { name: titulo, exact: true })).toBeVisible();
  }
});

test('Ayuda: el Developer ve todas las secciones', async () => {
  await ir(pagina, '/ayuda');
  await expect(pagina.locator('.ay-seccion-titulo')).toHaveText([
    'Lo básico', 'Asistente', 'Documentos', 'Perfil fiscal', 'Actividad', 'Organizaciones', 'Personas', 'Roles y marca', 'Operación',
  ]);
});

test.describe('sin sesión', () => {
  test('un enlace de registro inválido lo dice claro', async ({ browser }) => {
    const anonimo = await browser.newContext();
    const p = await anonimo.newPage();
    await p.goto('/registro#t=estonoesuntokenvalidoparanada123');
    await expect(p.getByRole('heading', { name: 'Este enlace ya no sirve' })).toBeVisible({ timeout: 30_000 });
    await anonimo.close();
  });
});

test('Organizaciones: se crea una y se borra', async () => {
  await pagina.setViewportSize({ width: 1280, height: 900 });
  await ponerTema(pagina, 'claro');
  await ir(pagina, '/organizaciones');

  await pagina.getByRole('button', { name: 'Nueva organización' }).click();
  await pagina.locator('input[name=nombre]').fill('Cliente E2E');
  await pagina.getByRole('button', { name: 'Crear organización' }).click();
  await expect(pagina.locator('.or-item', { hasText: 'Cliente E2E' })).toBeVisible();
  await expect(pagina.getByRole('heading', { name: 'Personas' })).toBeVisible();

  // Limpieza: la base de desarrollo no se queda con datos de prueba.
  await ir(pagina, '/organizaciones');
  await pagina.locator('.or-item', { hasText: 'Cliente E2E' }).click();
  await pagina.getByRole('button', { name: 'Eliminar organización' }).click();
  await pagina.getByRole('button', { name: 'Eliminar', exact: true }).last().click();
  await expect(pagina.locator('.or-item', { hasText: 'Cliente E2E' })).toHaveCount(0);
});

test('Alta de un cliente: pide organización, deja crearla ahí mismo y la enseña en su ficha', async () => {
  await pagina.setViewportSize({ width: 1280, height: 900 });
  await ir(pagina, '/identity/users');
  await pagina.click('button:has-text("Crear a mano")');
  await expect(pagina.locator('.us-rol.elegido strong')).toHaveText(['Usuario']);

  await pagina.locator('#us-usuario').fill('cliente.e2e');
  await pagina.locator('#us-correo').fill('cliente.e2e@auxiliar.test');
  await pagina.locator('#us-clave').fill('1q2w3E*!e2e');

  // Un cliente sin organización no vería nada: hasta elegirla no se puede crear.
  const crear = pagina.locator('.modal-footer button:has-text("Crear usuario")');
  await expect(pagina.locator('app-selector-organizacion')).toContainText('obligatoria');
  await expect(crear).toBeDisabled();

  // Al equipo no se le pide.
  await pagina.locator('.us-rol', { has: pagina.locator('strong', { hasText: /^Administrador$/ }) }).click();
  await expect(pagina.locator('app-selector-organizacion')).toHaveCount(0);
  await pagina.locator('.us-rol', { has: pagina.locator('strong', { hasText: /^Usuario$/ }) }).click();

  await pagina.locator('app-selector-organizacion button:has-text("Nueva")').click();
  await pagina.locator('input[name=so-nueva]').fill('Cliente Alta E2E');
  await pagina.locator('app-selector-organizacion button:has-text("Crear")').click();
  await expect(pagina.locator('#us-organizacion')).toHaveValue(/.+/);
  await expect(crear).toBeEnabled();
  await crear.click();

  const ficha = pagina.locator('.us-fila, li, tr', { hasText: 'cliente.e2e' }).first();
  await expect(ficha.locator('.us-org')).toHaveText('Cliente Alta E2E');

  // Limpieza: el usuario y la organización de prueba.
  await pagina.getByRole('button', { name: 'Eliminar a cliente.e2e' }).click();
  await pagina.getByRole('button', { name: 'Eliminar', exact: true }).last().click();
  await expect(pagina.getByRole('button', { name: 'Eliminar a cliente.e2e' })).toHaveCount(0);
  await ir(pagina, '/organizaciones');
  await pagina.locator('.or-item', { hasText: 'Cliente Alta E2E' }).click();
  await pagina.getByRole('button', { name: 'Eliminar organización' }).click();
  await pagina.getByRole('button', { name: 'Eliminar', exact: true }).last().click();
  await expect(pagina.locator('.or-item', { hasText: 'Cliente Alta E2E' })).toHaveCount(0);
});

test('Invitar a un cliente pide su organización; al equipo no', async () => {
  await pagina.setViewportSize({ width: 1280, height: 900 });
  await ir(pagina, '/invitaciones');
  await pagina.locator('.inv-rol', { has: pagina.locator('strong', { hasText: /^Usuario$/ }) }).click();
  await expect(pagina.locator('app-selector-organizacion')).toBeVisible();
  await pagina.locator('.inv-rol', { has: pagina.locator('strong', { hasText: /^Administrador$/ }) }).click();
  await expect(pagina.locator('app-selector-organizacion')).toHaveCount(0);
});
