// Primera hoja de un Excel (.xlsx, .xls) como filas de texto. La biblioteca se carga solo cuando hace falta.

export async function filasDeExcel(contenido: ArrayBuffer): Promise<string[][]> {
  const XLSX = await import('xlsx');
  const libro = XLSX.read(contenido, { type: 'array' });
  const hoja = libro.Sheets[libro.SheetNames[0]];
  if (!hoja) {
    return [];
  }
  const filas = XLSX.utils.sheet_to_json<unknown[]>(hoja, { header: 1, raw: true, defval: '', blankrows: false });
  return filas.map(f => f.map(c => (c === null || c === undefined ? '' : String(c))));
}

export const ES_EXCEL = (nombre: string) => /\.xls[xm]?$/i.test(nombre);
