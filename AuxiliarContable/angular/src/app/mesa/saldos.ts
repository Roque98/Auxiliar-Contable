import { SaldoDeAperturaDto } from './mesa.service';

/**
 * Saldos pegados de Excel: un renglón por cuenta, «cuenta, deudor, acreedor» separados por tabulador o punto y coma.
 * Las comas de miles se quitan. Un renglón que no se entiende cuenta como malo.
 */
export function leerSaldos(texto: string): { saldos: SaldoDeAperturaDto[]; deudores: number; acreedores: number; malos: number } {
  const saldos: SaldoDeAperturaDto[] = [];
  let malos = 0;
  for (const renglon of texto.split(/\r?\n/)) {
    if (!renglon.trim()) {
      continue;
    }
    const [cuenta, deudor, acreedor] = renglon.split(/\t|;/).map(x => x.trim());
    const n = (x: string | undefined) => (x ? Number(x.replace(/[$,\s]/g, '')) : 0);
    const d = n(deudor);
    const a = n(acreedor);
    if (!/^\d{3}(\.\d{2,3})?$/.test(cuenta ?? '') || Number.isNaN(d) || Number.isNaN(a) || d < 0 || a < 0 || (d > 0 && a > 0)) {
      malos++;
      continue;
    }
    if (d || a) {
      saldos.push({ cuenta, deudor: d, acreedor: a });
    }
  }
  const suma = (k: 'deudor' | 'acreedor') => Math.round(saldos.reduce((s, x) => s + x[k], 0) * 100) / 100;
  return { saldos, deudores: suma('deudor'), acreedores: suma('acreedor'), malos };
}
