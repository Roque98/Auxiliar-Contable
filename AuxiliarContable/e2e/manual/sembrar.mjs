// Siembra datos de EJEMPLO en la consola LOCAL para el manual: una cuenta por rol, tres clientes
// ficticios con perfil y documentos. Idempotente. NUNCA correr contra producción.
//   node manual/sembrar.mjs
import { chromium } from '@playwright/test';
import { CUENTAS, CLIENTES, API, entrar } from './comun.mjs';

if (!/localhost|127\.0\.0\.1/.test(API)) {
  throw new Error(`Solo contra la consola local (API = ${API}).`);
}
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'; // certificado de desarrollo, solo localhost

const navegador = await chromium.launch({ args: ['--ignore-certificate-errors'] });
const { token } = await entrar(navegador, CUENTAS.developer.usuario, CUENTAS.developer.clave);
const h = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', Accept: 'application/json' };

async function llamar(metodo, ruta, cuerpo) {
  const r = await fetch(`${API}/${ruta}`, { method: metodo, headers: h, body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo) });
  const texto = await r.text();
  if (!r.ok) {
    throw new Error(`${metodo} ${ruta} -> ${r.status} ${texto.slice(0, 300)}`);
  }
  return texto ? JSON.parse(texto) : null;
}

async function subir(ruta, nombre, bytes) {
  const datos = new FormData();
  datos.append('archivo', new Blob([bytes]), nombre);
  const r = await fetch(`${API}/${ruta}`, { method: 'POST', headers: { Authorization: h.Authorization }, body: datos });
  if (!r.ok) {
    console.log(`  (no se subió ${nombre}: ${r.status} ${(await r.text()).slice(0, 160)})`);
  }
}

// --- Clientes (organizaciones) -------------------------------------------------------------
const existentes = (await llamar('GET', 'api/app/organizacion?MaxResultCount=100')).items;
const orgs = {};
for (const cli of CLIENTES) {
  let org = existentes.find(o => o.nombre === cli.nombre);
  if (!org) {
    org = await llamar('POST', 'api/app/organizacion', { nombre: cli.nombre, descripcion: cli.descripcion });
    console.log('cliente creado:', cli.nombre);
  }
  orgs[cli.nombre] = org.id;
}

// --- Cuentas por rol (la de cliente nace dentro de su organización) ------------------------
const ids = {};
for (const c of Object.values(CUENTAS).filter(c => c.usuario !== 'admin')) {
  const lista = await llamar('GET', `api/identity/users?Filter=${encodeURIComponent(c.usuario)}`);
  let u = lista.items.find(x => x.userName === c.usuario);
  if (!u) {
    const deCliente = CLIENTES.find(x => x.personas && c.usuario in x.personas);
    u = await llamar('POST', 'api/identity/users', {
      userName: c.usuario, name: c.nombre, surname: c.apellido, email: c.correo, password: c.clave,
      roleNames: [c.rol], isActive: true, lockoutEnabled: true,
      extraProperties: deCliente ? { OrganizacionId: orgs[deCliente.nombre] } : {},
    });
    console.log('cuenta creada:', c.usuario, `(${c.rol})`);
  }
  ids[c.usuario] = u.id;
}

// --- Papel en su organización, perfil y documentos -----------------------------------------
for (const cli of CLIENTES) {
  const id = orgs[cli.nombre];
  for (const [usuario, rol] of Object.entries(cli.personas ?? {})) {
    await llamar('POST', `api/app/organizacion/${id}/agregar-miembro`, { userId: ids[usuario], rol }).catch(() => undefined);
    await llamar('POST', `api/app/organizacion/${id}/cambiar-rol-de-miembro/${ids[usuario]}`, { rol }).catch(e => console.log('  papel:', e.message.slice(0, 160)));
  }
  for (const [campo, valor] of Object.entries(cli.perfil ?? {})) {
    await llamar('POST', `api/app/perfil-fiscal/responder/${id}`, { campo, valor }).catch(e => console.log('  perfil:', e.message.slice(0, 160)));
  }
  for (const d of cli.documentos ?? []) {
    await subir(`api/app/boveda/subir/${id}?tipo=${d.tipo}`, d.nombre, d.bytes());
  }
  if (cli.constancia) {
    // La lee el motor fiscal al subirla: hace falta MotorFiscal:Url (ver docs/manuales).
    await subir(`api/app/perfil-fiscal/subir-csf/${id}`, cli.constancia.nombre, cli.constancia.bytes());
  }
}

await navegador.close();
console.log('listo');
