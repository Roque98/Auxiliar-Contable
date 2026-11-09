// Datos de ejemplo y utilidades compartidas por sembrar.mjs y capturar.mjs. Todo es FICTICIO:
// nombres, correos @ejemplo.mx y RFC de prueba. Solo para la consola local.

export const WEB = process.env.AUXILIAR_E2E_URL ?? 'http://localhost:4201';
export const API = process.env.AUXILIAR_E2E_API ?? 'https://localhost:44338';

const CLAVE = process.env.AUXILIAR_MANUAL_CLAVE ?? 'Ejemplo-2026!';

/** Una cuenta por rol, con el mismo nombre que usa la ayuda de la consola. */
export const CUENTAS = {
  developer: { usuario: 'admin', clave: '1q2w3E*', rol: 'admin', etiqueta: 'Developer' },
  owner: { usuario: 'duena', clave: CLAVE, rol: 'owner', etiqueta: 'Owner', nombre: 'Marta', apellido: 'Salinas (ejemplo)', correo: 'marta.salinas@ejemplo.mx' },
  administrador: { usuario: 'contador', clave: CLAVE, rol: 'administrador', etiqueta: 'Administrador', nombre: 'Jorge', apellido: 'Ibarra (ejemplo)', correo: 'jorge.ibarra@ejemplo.mx' },
  usuario: { usuario: 'lupita', clave: CLAVE, rol: 'usuario', etiqueta: 'Usuario', nombre: 'Lupita', apellido: 'Herrera (ejemplo)', correo: 'lupita.herrera@ejemplo.mx' },
};

const PAPEL = { lector: 0, miembro: 1, administrador: 2 };

const XML = n => new TextEncoder().encode(
  `<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" Folio="${n}"/>`);
const PDF = t => new TextEncoder().encode(`%PDF-1.4\n% ${t} (documento de ejemplo)\n%%EOF\n`);

export const CLIENTES = [
  {
    nombre: 'Panadería La Espiga',
    descripcion: 'Cliente de ejemplo para el manual',
    personas: { lupita: PAPEL.administrador },
    perfil: {
      tipo_persona: 'fisica',
      rfc: 'GODE561231GR8',
      nombre: 'Guadalupe Herrera (ejemplo)',
      codigo_postal: '06600',
      giro: 'Panadería y repostería',
      emite_facturas: 'si',
      bancos: '["BBVA","Banorte"]',
      tiene_trabajadores: 'si',
    },
    documentos: [
      { nombre: 'factura-0001.xml', tipo: 2, bytes: () => XML(1) },
      { nombre: 'factura-0002.xml', tipo: 2, bytes: () => XML(2) },
      { nombre: 'BBVA-octubre.pdf', tipo: 3, bytes: () => PDF('Estado de cuenta octubre') },
      { nombre: 'ticket-harina.pdf', tipo: 4, bytes: () => PDF('Ticket de compra') },
    ],
  },
  {
    nombre: 'Taller Mecánico Ruiz',
    descripcion: 'Cliente de ejemplo para el manual',
    perfil: { tipo_persona: 'moral', giro: 'Reparación de automóviles' },
    documentos: [{ nombre: 'factura-refacciones.xml', tipo: 2, bytes: () => XML(31) }],
  },
  {
    nombre: 'Consultoría Vega',
    descripcion: 'Cliente de ejemplo para el manual',
    perfil: {},
    documentos: [],
  },
];

/** Entra con usuario y contraseña en un contexto nuevo y devuelve el token de acceso de la API. */
export async function entrar(navegador, usuario, clave, opciones = {}) {
  const contexto = await navegador.newContext({ ignoreHTTPSErrors: true, locale: 'es-MX', ...opciones });
  const pagina = await contexto.newPage();
  await pagina.goto(`${WEB}/account/login`);
  await pagina.waitForURL(/\/Account\/Login/i, { timeout: 60_000 });
  await pagina.locator('#LoginInput_UserNameOrEmailAddress').evaluate((el, v) => (el.value = v), usuario);
  await pagina.locator('#LoginInput_Password').evaluate((el, v) => (el.value = v), clave);
  await pagina.click('button[name=Action]');
  await pagina.waitForURL(u => !/\/Account\//i.test(u.pathname), { timeout: 60_000 });
  await pagina.waitForTimeout(3000);
  // El token de acceso vive en memoria; el de renovación en localStorage: se canjea por uno nuevo.
  const refresh = await pagina.evaluate(() => localStorage.getItem('refresh_token'));
  let token = null;
  if (refresh) {
    const r = await contexto.request.post(`${API}/connect/token`, {
      form: { grant_type: 'refresh_token', client_id: 'AuxiliarContable_App', refresh_token: refresh },
    });
    token = (await r.json()).access_token ?? null;
  }
  return { contexto, pagina, token };
}
