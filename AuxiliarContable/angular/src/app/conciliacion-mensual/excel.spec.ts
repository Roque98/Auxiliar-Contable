import { describe, expect, it } from 'vitest';
import * as XLSX from 'xlsx';
import { aApertura, catalogoAXml, leerBalanzaTabla, leerCatalogo, leerCatalogoTabla } from './balanza';
import { filasDeExcel } from './excel';

function libro(filas: unknown[][]): ArrayBuffer {
  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(filas), 'Hoja1');
  return XLSX.write(wb, { type: 'array', bookType: 'xlsx' }) as ArrayBuffer;
}

describe('balanza y catálogo en Excel', () => {
  it('balanza de Excel con título arriba, saldos inicial y final, y renglón de totales', async () => {
    const filas = await filasDeExcel(libro([
      ['Constructora de prueba'], ['Balanza de comprobación al 30/sep/2026'], [],
      ['Cuenta', 'Nombre', 'Saldo inicial deudor', 'Saldo inicial acreedor', 'Cargos', 'Abonos', 'Saldo final deudor', 'Saldo final acreedor'],
      ['1100-001', 'Bancos', 10, 0, 0, 0, 50000, 0],
      ['3100-001', 'Capital', 0, 10, 0, 0, 0, 50000],
      ['', 'Totales', 10, 10, 0, 0, 50000, 50000],
    ]));
    const b = leerBalanzaTabla(filas);
    expect(b.renglones).toEqual([
      { numCta: '1100-001', saldoFin: 50000, deudor: 50000, acreedor: 0 },
      { numCta: '3100-001', saldoFin: -50000, deudor: 0, acreedor: 50000 },
    ]);
  });

  it('catálogo de Excel sin columna de padre: la deduce por prefijo; se guarda como XML del Anexo 24', async () => {
    const filas = await filasDeExcel(libro([
      ['Catálogo de cuentas'],
      ['No. de cuenta', 'Nombre', 'Código agrupador', 'Naturaleza'],
      ['1100', 'Bancos', '102', 'Deudora'],
      ['1100-001', 'Bancos nacionales', '102.01', 'Deudora'],
      ['3100', 'Capital', '301', 'Acreedora'],
      ['3100-001', 'Capital fijo', '301.01', 'Acreedora'],
    ]));
    const cuentas = leerCatalogoTabla(filas);
    expect(cuentas.length).toBe(4);
    expect(cuentas[1]).toEqual({ numCta: '1100-001', codAgrup: '102.01', descripcion: 'Bancos nacionales', subCtaDe: '1100', natur: 'D' });
    const xml = catalogoAXml(cuentas, '', 2026, 9);
    expect(xml).toContain('NumCta="1100-001"');
    expect(xml).toContain('Nivel="2"');
    expect(leerCatalogo(xml)).toEqual(cuentas);
    const ap = aApertura(leerBalanzaTabla([['Cuenta', 'Deudor', 'Acreedor'], ['1100-001', '50000', '0'], ['3100-001', '0', '50000']]), cuentas);
    expect(ap.saldos).toEqual([{ cuenta: '102.01', deudor: 50000, acreedor: 0 }, { cuenta: '301.01', deudor: 0, acreedor: 50000 }]);
  });

  it('sin encabezado reconocible no inventa', () => {
    expect(leerCatalogoTabla([['a', 'b'], ['1', '2']])).toEqual([]);
    expect(() => leerBalanzaTabla([['a', 'b']])).toThrow();
  });
});
