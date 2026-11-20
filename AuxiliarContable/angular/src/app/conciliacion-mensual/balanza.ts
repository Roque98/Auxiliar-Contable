// Catálogo de cuentas y balanza de comprobación que trae el auxiliar (XML de la contabilidad electrónica del SAT o
// Excel o CSV) -> saldos de apertura por código agrupador, que es lo que guarda la mesa (GuardarApertura). Puro: sin Angular.

export interface CuentaDelCatalogo {
  numCta: string;
  codAgrup: string;
  descripcion: string;
  subCtaDe: string | null;
  /** D = deudora, A = acreedora. */
  natur: 'D' | 'A' | null;
}

export interface RenglonDeBalanza {
  numCta: string;
  /** Saldo final con el signo de la naturaleza de la cuenta (positivo = saldo normal). */
  saldoFin: number;
  /** Del CSV: saldo final ya separado en deudor y acreedor. */
  deudor?: number;
  acreedor?: number;
}

export interface BalanzaLeida {
  anio: number | null;
  mes: number | null;
  renglones: RenglonDeBalanza[];
}

export interface SaldoDeApertura {
  cuenta: string;
  deudor: number;
  acreedor: number;
}

export interface Apertura {
  saldos: SaldoDeApertura[];
  deudores: number;
  acreedores: number;
  /** Cuentas que no se pudieron llevar a un código agrupador (no se suman). */
  sinCodigo: string[];
}

const ATRIBUTO = /([A-Za-z_][\w.-]*)\s*=\s*"([^"]*)"/g;

function atributos(texto: string): Record<string, string> {
  const r: Record<string, string> = {};
  for (const m of texto.matchAll(ATRIBUTO)) {
    r[m[1]] = m[2].replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
  }
  return r;
}

function elementos(xml: string, nombre: string): Record<string, string>[] {
  const re = new RegExp(`<(?:[\\w-]+:)?${nombre}\\b([^>]*?)/?>`, 'g');
  return [...xml.matchAll(re)].map(m => atributos(m[1]));
}

function numero(v: string | undefined): number {
  const n = Number(String(v ?? '').replace(/[$,\s]/g, ''));
  return Number.isFinite(n) ? n : 0;
}

/** Catálogo de cuentas en XML del SAT (catalogocuentas:Catalogo, nodos Ctas). */
export function leerCatalogo(xml: string): CuentaDelCatalogo[] {
  return elementos(xml, 'Ctas')
    .filter(a => a['NumCta'])
    .map(a => ({
      numCta: a['NumCta'].trim(),
      codAgrup: (a['CodAgrup'] ?? '').trim(),
      descripcion: (a['Desc'] ?? '').trim(),
      subCtaDe: a['SubCtaDe']?.trim() || null,
      natur: a['Natur'] === 'D' || a['Natur'] === 'A' ? a['Natur'] : null,
    }));
}

/** Balanza en XML del SAT (BCE:Balanza, nodos Ctas con SaldoFin). */
export function leerBalanzaXml(xml: string): BalanzaLeida {
  const raiz = elementos(xml, 'Balanza')[0] ?? {};
  return {
    anio: raiz['Anio'] ? Number(raiz['Anio']) : null,
    mes: raiz['Mes'] ? Number(raiz['Mes']) : null,
    renglones: elementos(xml, 'Ctas')
      .filter(a => a['NumCta'])
      .map(a => ({ numCta: a['NumCta'].trim(), saldoFin: numero(a['SaldoFin']) })),
  };
}

/** Texto CSV (coma, punto y coma o tabulador, según el primer renglón) -> filas de celdas. */
export function partirCsv(texto: string): string[][] {
  const lineas = texto.replace(/^\uFEFF/, '').split(/\r?\n/).filter(l => l.trim());
  if (!lineas.length) {
    return [];
  }
  const sep = [';', '\t', ','].find(s => lineas[0].includes(s)) ?? ',';
  return lineas.map(l => l.split(sep).map(c => c.trim().replace(/^"|"$/g, '')));
}

const sinAcentos = (c: string) => String(c ?? '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').trim();

/** Busca el encabezado en los primeros renglones (los reportes de Excel traen título, RFC y fecha arriba). */
function encabezado(filas: string[][], ...exige: ((c: string) => boolean)[]): { i: number; enc: string[] } | null {
  for (let i = 0; i < Math.min(filas.length, 30); i++) {
    const enc = filas[i].map(sinAcentos);
    if (exige.every(f => enc.some(f))) {
      return { i, enc };
    }
  }
  return null;
}

const esCuenta = (c: string) => /^(cuenta|no\.? ?(de )?cuenta|num ?cta|numcta|numero|codigo)\b/.test(c) && !/agrup|sat|padre/.test(c);

/**
 * Balanza en CSV o Excel: una columna de cuenta y, o bien «deudor» y «acreedor» (los del saldo final), o bien «saldo
 * final». El encabezado decide las columnas; renglones sin número de cuenta (títulos, totales) se saltan.
 */
export function leerBalanzaTabla(filas: string[][]): BalanzaLeida {
  const h = encabezado(filas, esCuenta, c => /deudor|acreedor|saldo/.test(c));
  if (!h) {
    throw new Error('La balanza necesita una columna «cuenta» y «deudor» y «acreedor» (o «saldo final»).');
  }
  const { enc } = h;
  // Por prioridad: «saldo final» gana a un «saldo» suelto (y nunca toma lo inicial).
  const col = (...nombres: string[]) => {
    for (const n of nombres) {
      const i = enc.findIndex(c => c.includes(n) && !c.includes('inicial') && !c.includes('anterior'));
      if (i >= 0) {
        return i;
      }
    }
    return -1;
  };
  const iCuenta = enc.findIndex(esCuenta);
  const iDeudor = col('final deudor', 'deudor final', 'deudor');
  const iAcreedor = col('final acreedor', 'acreedor final', 'acreedor');
  const iFinal = col('saldo final', 'saldofin', 'saldo');
  if (iFinal < 0 && (iDeudor < 0 || iAcreedor < 0)) {
    throw new Error('La balanza necesita una columna «cuenta» y «deudor» y «acreedor» (o «saldo final»).');
  }
  const renglones: RenglonDeBalanza[] = [];
  for (const c of filas.slice(h.i + 1)) {
    const numCta = String(c[iCuenta] ?? '').trim();
    if (!/\d/.test(numCta)) {
      continue;
    }
    if (iDeudor >= 0 && iAcreedor >= 0) {
      const deudor = numero(c[iDeudor]);
      const acreedor = numero(c[iAcreedor]);
      renglones.push({ numCta, saldoFin: deudor - acreedor, deudor, acreedor });
    } else {
      renglones.push({ numCta, saldoFin: numero(c[iFinal]) });
    }
  }
  return { anio: null, mes: null, renglones };
}

export function leerBalanzaCsv(texto: string): BalanzaLeida {
  return leerBalanzaTabla(partirCsv(texto));
}

/**
 * Catálogo de cuentas en Excel o CSV (exportación de CONTPAQi, Aspel COI…): cuenta y nombre; si los trae, código
 * agrupador, naturaleza y cuenta padre. Sin columna de padre, se deduce por prefijo del número.
 */
export function leerCatalogoTabla(filas: string[][]): CuentaDelCatalogo[] {
  const h = encabezado(filas, esCuenta, c => /^(nombre|descrip)/.test(c));
  if (!h) {
    return [];
  }
  const { enc } = h;
  const iCuenta = enc.findIndex(esCuenta);
  const iNombre = enc.findIndex(c => /^(nombre|descrip)/.test(c));
  const iAgrup = enc.findIndex(c => /agrupador|cod\.? ?agrup|codigo sat/.test(c));
  const iNatur = enc.findIndex(c => /^natur/.test(c));
  const iPadre = enc.findIndex(c => /padre|subcuenta de|subctade/.test(c));
  const cuentas: CuentaDelCatalogo[] = [];
  for (const c of filas.slice(h.i + 1)) {
    const numCta = String(c[iCuenta] ?? '').trim();
    if (!/\d/.test(numCta)) {
      continue;
    }
    const ag = iAgrup >= 0 ? String(c[iAgrup] ?? '').trim() : '';
    const na = iNatur >= 0 ? sinAcentos(c[iNatur])[0] : '';
    cuentas.push({
      numCta,
      codAgrup: /^\d{3}(\.\d{2,3})?$/.test(ag) ? ag : '',
      descripcion: String(c[iNombre] ?? '').trim(),
      subCtaDe: iPadre >= 0 ? String(c[iPadre] ?? '').trim() || null : null,
      natur: na === 'd' ? 'D' : na === 'a' ? 'A' : null,
    });
  }
  if (iPadre < 0) {
    const nums = cuentas.map(x => x.numCta).sort((a, b) => b.length - a.length);
    for (const x of cuentas) {
      x.subCtaDe = nums.find(n => n !== x.numCta && n.length < x.numCta.length && x.numCta.startsWith(n)) ?? null;
    }
  }
  return cuentas;
}

const escapar = (t: string) => t.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** El catálogo como XML del Anexo 24, para guardarlo en la bóveda y que el motor lo lea igual que el del SAT. */
export function catalogoAXml(cuentas: CuentaDelCatalogo[], rfc = '', anio = new Date().getFullYear(), mes = new Date().getMonth() + 1): string {
  const nivel = (c: CuentaDelCatalogo): number => {
    let n = 1;
    let p = c.subCtaDe;
    const vistos = new Set<string>();
    while (p && !vistos.has(p)) {
      vistos.add(p);
      n++;
      p = cuentas.find(x => x.numCta === p)?.subCtaDe ?? null;
    }
    return n;
  };
  const ctas = cuentas.map(c =>
    `<catalogocuentas:Ctas CodAgrup="${escapar(c.codAgrup)}" NumCta="${escapar(c.numCta)}" Desc="${escapar(c.descripcion)}"`
    + (c.subCtaDe ? ` SubCtaDe="${escapar(c.subCtaDe)}"` : '')
    + ` Nivel="${nivel(c)}" Natur="${c.natur ?? (c.codAgrup ? naturalezaDelCodigo(c.codAgrup) : 'D')}"/>`);
  return '<?xml version="1.0" encoding="UTF-8"?>\n'
    + '<catalogocuentas:Catalogo xmlns:catalogocuentas="http://www.sat.gob.mx/esquemas/ContabilidadE/1_3/CatalogoCuentas" '
    + `Version="1.3" RFC="${escapar(rfc)}" Mes="${String(mes).padStart(2, '0')}" Anio="${anio}">\n`
    + ctas.join('\n') + '\n</catalogocuentas:Catalogo>\n';
}

// Naturaleza por código agrupador (Anexo 24): por grupo (1, 5, 6, 7, 8 deudoras; 2, 3, 4 acreedoras) salvo estas,
// sacadas del catálogo del código agrupador (depreciaciones y estimaciones, devoluciones y descuentos, cuentas de
// orden «contra»…).
const ACREEDORAS_EN_GRUPO_DEUDOR = ['108', '116', '171', '172', '183', '189', '503', '702', '704'];
const DEUDORAS_EN_GRUPO_ACREEDOR = ['402', '304.02', '305.02'];

export function naturalezaDelCodigo(codigo: string): 'D' | 'A' {
  const grupo = codigo.split('.')[0];
  if (DEUDORAS_EN_GRUPO_ACREEDOR.some(c => codigo === c || grupo === c)) {
    return 'D';
  }
  if (ACREEDORAS_EN_GRUPO_DEUDOR.includes(grupo) || /^8\d\d\.02$/.test(codigo)) {
    return 'A';
  }
  return '234'.includes(codigo[0]) ? 'A' : 'D';
}

/** Código agrupador válido para la apertura (como 102.01). */
export const CODIGO = /^\d{3}(\.\d{2,3})?$/;

/**
 * La balanza (al cierre de un mes) como saldos de apertura del mes siguiente, por código agrupador. Solo se suman las
 * cuentas de último nivel (las acumulativas repetirían el saldo). Con catálogo, cada cuenta va a su código agrupador y
 * su naturaleza; sin él, la cuenta de la balanza tiene que ser ya el código agrupador.
 */
export function aApertura(balanza: BalanzaLeida, catalogo: CuentaDelCatalogo[] | null): Apertura {
  const porCuenta = new Map((catalogo ?? []).map(c => [c.numCta, c]));
  const cuentas = balanza.renglones.map(r => r.numCta);
  const padres = new Set<string>();
  if (catalogo?.length) {
    for (const c of catalogo) {
      if (c.subCtaDe) {
        padres.add(c.subCtaDe);
      }
    }
  }
  // Sin SubCtaDe: es padre la que es prefijo de otra («102» de «102.01», «1020» de «1020-001»).
  for (const a of cuentas) {
    if (cuentas.some(b => b !== a && b.startsWith(a) && /^[.\-_ ]/.test(b.slice(a.length)))) {
      padres.add(a);
    }
  }

  const suma = new Map<string, number>();
  const sinCodigo: string[] = [];
  for (const r of balanza.renglones) {
    if (padres.has(r.numCta)) {
      continue;
    }
    const c = porCuenta.get(r.numCta);
    const codigo = (c?.codAgrup || (catalogo?.length ? '' : r.numCta)).trim();
    if (!CODIGO.test(codigo)) {
      if (Math.abs(r.saldoFin) >= 0.005 || (r.deudor ?? 0) || (r.acreedor ?? 0)) {
        sinCodigo.push(r.numCta);
      }
      continue;
    }
    // Saldo con signo «deudor positivo».
    const deudorNeto = r.deudor !== undefined || r.acreedor !== undefined
      ? (r.deudor ?? 0) - (r.acreedor ?? 0)
      : (c?.natur ?? naturalezaDelCodigo(codigo)) === 'D' ? r.saldoFin : -r.saldoFin;
    suma.set(codigo, (suma.get(codigo) ?? 0) + deudorNeto);
  }

  const saldos: SaldoDeApertura[] = [...suma.entries()]
    .map(([cuenta, neto]) => {
      const n = Math.round(neto * 100) / 100;
      return { cuenta, deudor: n > 0 ? n : 0, acreedor: n < 0 ? -n : 0 };
    })
    .filter(s => s.deudor || s.acreedor)
    .sort((a, b) => a.cuenta.localeCompare(b.cuenta, 'es', { numeric: true }));
  const redondo = (x: number) => Math.round(x * 100) / 100;
  return {
    saldos,
    deudores: redondo(saldos.reduce((t, s) => t + s.deudor, 0)),
    acreedores: redondo(saldos.reduce((t, s) => t + s.acreedor, 0)),
    sinCodigo,
  };
}

/** El mes siguiente al de la balanza: los saldos finales de enero son la apertura de febrero. */
export function mesSiguiente(anio: number, mes: number): { anio: number; mes: number } {
  return mes === 12 ? { anio: anio + 1, mes: 1 } : { anio, mes: mes + 1 };
}
