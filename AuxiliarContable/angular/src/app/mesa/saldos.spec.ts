import { describe, expect, it } from 'vitest';
import { leerSaldos } from './saldos';

describe('leerSaldos', () => {
  it('lee lo pegado de Excel: tabuladores, comas de miles y renglones vacíos', () => {
    const r = leerSaldos('102.01\t150,000.00\t\n\n201.01\t\t$35,000\n301.01;;115000');
    expect(r.saldos).toEqual([
      { cuenta: '102.01', deudor: 150000, acreedor: 0 },
      { cuenta: '201.01', deudor: 0, acreedor: 35000 },
      { cuenta: '301.01', deudor: 0, acreedor: 115000 },
    ]);
    expect(r.deudores).toBe(150000);
    expect(r.acreedores).toBe(150000);
    expect(r.malos).toBe(0);
  });

  it('cuenta como malo lo que no se entiende o trae los dos saldos', () => {
    expect(leerSaldos('Bancos\t100\n102.01\t5\t5\n102.01\tabc').malos).toBe(3);
  });
});
