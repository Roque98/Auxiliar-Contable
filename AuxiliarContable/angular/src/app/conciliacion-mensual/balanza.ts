// Catálogo de cuentas y balanza de comprobación que trae el auxiliar (XML de la contabilidad electrónica del SAT o
// CSV) -> saldos de apertura por código agrupador, que es lo que guarda la mesa (GuardarApertura). Puro: sin Angular.

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

/**
 * Balanza en CSV (de Excel): una columna de cuenta y, o bien «deudor» y «acreedor», o bien «saldo final». Separador
 * coma, punto y coma o tabulador; el encabezado decide las columnas.
 */
export function leerBalanzaCsv(texto: string): BalanzaLeida {
  const lineas = texto.replace(/^﻿/, '').split(/\r?\n/).filter(l => l.trim());
  if (!lineas.length) {
    return { anio: null, mes: null, renglones: [] };
  }
  const sep = [';', '\t', ','].find(s => lineas[0].includes(s)) ?? ',';
  const partir = (l: string) => l.split(sep).map(c => c.trim().replace(/^"|"$/g, ''));
  const enc = partir(lineas[0]).map(c => c.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''));
  // Por prioridad: «saldo final» gana a un «saldo» suelto (y nunca toma «saldo inicial»).
  const col = (...nombres: string[]) => {
    for (const n of nombres) {
      const i = enc.findIndex(c => c.includes(n) && !c.includes('inicial'));
      if (i >= 0) {
        return i;
      }
    }
    return -1;
  };
  const iCuenta = col('cuenta', 'numcta', 'codigo');
  const iDeudor = col('deudor');
  const iAcreedor = col('acreedor');
  const iFinal = col('saldo final', 'saldofin', 'saldo');
  if (iCuenta < 0 || (iFinal < 0 && (iDeudor < 0 || iAcreedor < 0))) {
    throw new Error('El CSV necesita una columna «cuenta» y «deudor» y «acreedor» (o «saldo final»).');
  }
  const renglones: RenglonDeBalanza[] = [];
  for (const l of lineas.slice(1)) {
    const c = partir(l);
    const numCta = (c[iCuenta] ?? '').trim();
    if (!numCta) {
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
