/** Un cargo o abono del mes en una cuenta, como lo manda el motor (los mismos que suman en la balanza). */
export interface MovimientoDeAuxiliarDto {
  cuenta: string;
  fecha: string;
  poliza: string;
  tipo: string;
  concepto: string;
  texto: string;
  debe: number;
  haber: number;
  uuid?: string | null;
  rfc?: string | null;
}

export interface RenglonDeBalanzaDto {
  cuenta: string;
  nombre: string;
  nivel: number;
  naturaleza: string;
  saldoInicial: number;
  debe: number;
  haber: number;
  saldoFinal: number;
}

export interface Auxiliar {
  renglones: (MovimientoDeAuxiliarDto & { saldo: number })[];
  cargos: number;
  abonos: number;
  saldoFinal: number;
  /** El saldo corrido llega al saldo final de la balanza. */
  cuadra: boolean;
}

const centavos = (n: number) => Math.round(n * 100) / 100;

/**
 * El auxiliar (libro mayor) de una cuenta en el mes: parte del saldo inicial de la balanza y suma cada movimiento según
 * la naturaleza (deudora: cargo − abono; acreedora: abono − cargo). Una cuenta de mayor junta las de sus subcuentas.
 */
export function auxiliarDe(cuenta: RenglonDeBalanzaDto, movimientos: MovimientoDeAuxiliarDto[]): Auxiliar {
  const signo = cuenta.naturaleza === 'A' ? -1 : 1;
  let saldo = cuenta.saldoInicial;
  let cargos = 0;
  let abonos = 0;
  const renglones = movimientos
    .filter(m => m.cuenta === cuenta.cuenta || m.cuenta.startsWith(cuenta.cuenta + '.'))
    .map(m => {
      cargos += m.debe;
      abonos += m.haber;
      saldo = centavos(saldo + signo * (m.debe - m.haber));
      return { ...m, saldo };
    });
  return {
    renglones,
    cargos: centavos(cargos),
    abonos: centavos(abonos),
    saldoFinal: saldo,
    cuadra: Math.abs(saldo - cuenta.saldoFinal) < 0.01,
  };
}
