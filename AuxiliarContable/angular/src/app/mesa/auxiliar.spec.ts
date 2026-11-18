import { describe, expect, it } from 'vitest';
import { MovimientoDeAuxiliarDto, auxiliarDe } from './auxiliar';

const mov = (cuenta: string, debe: number, haber: number, poliza = 'D-2026-10-0001'): MovimientoDeAuxiliarDto => ({
  cuenta, fecha: '2026-10-05', poliza, tipo: 'Diario', concepto: 'x', texto: '', debe, haber,
});
const movs = [mov('102.01', 5800, 0), mov('102.01', 0, 1160, 'E-2026-10-0002'), mov('401.01', 0, 5000), mov('102.02', 100, 0)];

describe('auxiliarDe', () => {
  it('cuenta deudora: saldo corrido desde el inicial y cuadra con la balanza', () => {
    const a = auxiliarDe({ cuenta: '102.01', nombre: 'Bancos', nivel: 2, naturaleza: 'D', saldoInicial: 48840, debe: 5800, haber: 1160, saldoFinal: 53480 }, movs);
    expect(a.renglones.map(r => r.saldo)).toEqual([54640, 53480]);
    expect(a.cuadra).toBe(true);
  });

  it('cuenta acreedora: los abonos suben el saldo', () => {
    const a = auxiliarDe({ cuenta: '401.01', nombre: 'Ventas', nivel: 2, naturaleza: 'A', saldoInicial: 0, debe: 0, haber: 5000, saldoFinal: 5000 }, movs);
    expect(a.saldoFinal).toBe(5000);
    expect(a.cuadra).toBe(true);
  });

  it('la cuenta de mayor junta sus subcuentas, pero no las de otra cuenta que empieza igual', () => {
    const a = auxiliarDe({ cuenta: '102', nombre: 'Bancos', nivel: 1, naturaleza: 'D', saldoInicial: 0, debe: 5900, haber: 1160, saldoFinal: 4740 }, [...movs, mov('1020.01', 1, 0)]);
    expect(a.renglones.length).toBe(3);
    expect(a.cargos).toBe(5900);
    expect(a.cuadra).toBe(true);
  });

  it('si no llega al saldo final de la balanza lo dice', () => {
    expect(auxiliarDe({ cuenta: '102.01', nombre: '', nivel: 2, naturaleza: 'D', saldoInicial: 0, debe: 0, haber: 0, saldoFinal: 10 }, movs).cuadra).toBe(false);
  });
});
