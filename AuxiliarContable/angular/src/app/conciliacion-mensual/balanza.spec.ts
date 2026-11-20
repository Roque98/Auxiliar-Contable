import { describe, expect, it } from 'vitest';
import { aApertura, leerBalanzaCsv, leerBalanzaXml, leerCatalogo, mesSiguiente, naturalezaDelCodigo } from './balanza';

const CATALOGO = `<?xml version="1.0" encoding="UTF-8"?>
<catalogocuentas:Catalogo xmlns:catalogocuentas="http://www.sat.gob.mx/esquemas/ContabilidadE/1_3/CatalogoCuentas" Version="1.3" RFC="XAXX010101000" Mes="01" Anio="2026">
  <catalogocuentas:Ctas CodAgrup="102" NumCta="1100" Desc="Bancos" Nivel="1" Natur="D"/>
  <catalogocuentas:Ctas CodAgrup="102.01" NumCta="1100-01" Desc="Banco nacional" SubCtaDe="1100" Nivel="2" Natur="D"/>
  <catalogocuentas:Ctas CodAgrup="171.02" NumCta="1500-02" Desc="Dep. acum. equipo" Nivel="2" Natur="A"/>
  <catalogocuentas:Ctas CodAgrup="156.01" NumCta="1500-01" Desc="Equipo de transporte" Nivel="2" Natur="D"/>
  <catalogocuentas:Ctas CodAgrup="201.01" NumCta="2100-01" Desc="Proveedores" Nivel="2" Natur="A"/>
  <catalogocuentas:Ctas CodAgrup="301.01" NumCta="3100-01" Desc="Capital social" Nivel="2" Natur="A"/>
</catalogocuentas:Catalogo>`;

const BALANZA = `<BCE:Balanza xmlns:BCE="http://www.sat.gob.mx/esquemas/ContabilidadE/1_3/BalanzaComprobacion" Version="1.3" RFC="XAXX010101000" Mes="03" Anio="2026" TipoEnvio="N">
  <BCE:Ctas NumCta="1100" SaldoIni="0" Debe="0" Haber="0" SaldoFin="50000.00"/>
  <BCE:Ctas NumCta="1100-01" SaldoIni="0" Debe="0" Haber="0" SaldoFin="50000.00"/>
  <BCE:Ctas NumCta="1500-01" SaldoIni="0" Debe="0" Haber="0" SaldoFin="200000.00"/>
  <BCE:Ctas NumCta="1500-02" SaldoIni="0" Debe="0" Haber="0" SaldoFin="40000.00"/>
  <BCE:Ctas NumCta="2100-01" SaldoIni="0" Debe="0" Haber="0" SaldoFin="10000.00"/>
  <BCE:Ctas NumCta="3100-01" SaldoIni="0" Debe="0" Haber="0" SaldoFin="200000.00"/>
</BCE:Balanza>`;

describe('balanza y catálogo del auxiliar', () => {
  it('lee el catálogo y la balanza del SAT y los lleva a saldos de apertura que cuadran', () => {
    const cat = leerCatalogo(CATALOGO);
    expect(cat).toHaveLength(6);
    const b = leerBalanzaXml(BALANZA);
    expect(b.anio).toBe(2026);
    expect(b.mes).toBe(3);
    const a = aApertura(b, cat);
    expect(a.sinCodigo).toEqual([]);
    // La cuenta acumulativa 1100 no se suma dos veces; la depreciación acumulada va acreedora.
    expect(a.saldos).toEqual([
      { cuenta: '102.01', deudor: 50000, acreedor: 0 },
      { cuenta: '156.01', deudor: 200000, acreedor: 0 },
      { cuenta: '171.02', deudor: 0, acreedor: 40000 },
      { cuenta: '201.01', deudor: 0, acreedor: 10000 },
      { cuenta: '301.01', deudor: 0, acreedor: 200000 },
    ]);
    expect(a.deudores).toBe(250000);
    expect(a.acreedores).toBe(250000);
  });

  it('sin catálogo, la balanza va por código agrupador y la naturaleza sale del código', () => {
    const b = leerBalanzaXml(`<Balanza Mes="12" Anio="2025"><Ctas NumCta="102" SaldoFin="900"/><Ctas NumCta="102.01" SaldoFin="900"/>
      <Ctas NumCta="171.01" SaldoFin="100"/><Ctas NumCta="153.01" SaldoFin="200"/><Ctas NumCta="301.01" SaldoFin="1000"/></Balanza>`);
    const a = aApertura(b, null);
    expect(a.deudores).toBe(1100);
    expect(a.acreedores).toBe(1100);
  });

  it('marca lo que no tiene código agrupador', () => {
    const a = aApertura({ anio: null, mes: null, renglones: [{ numCta: '9999-X', saldoFin: 5 }] }, null);
    expect(a.sinCodigo).toEqual(['9999-X']);
    expect(a.saldos).toEqual([]);
  });

  it('lee un CSV de Excel con deudor y acreedor', () => {
    const b = leerBalanzaCsv('Cuenta;Descripción;Deudor;Acreedor\n102.01;Bancos;"1,500.00";0\n301.01;Capital;0;1500\n');
    const a = aApertura(b, null);
    expect(a.saldos).toEqual([
      { cuenta: '102.01', deudor: 1500, acreedor: 0 },
      { cuenta: '301.01', deudor: 0, acreedor: 1500 },
    ]);
    expect(() => leerBalanzaCsv('a,b\n1,2')).toThrow();
    const conInicial = leerBalanzaCsv('Cuenta,Saldo inicial,Cargos,Abonos,Saldo final\n102.01,10,5,0,15\n');
    expect(conInicial.renglones[0].saldoFin).toBe(15);
  });

  it('naturaleza y mes siguiente', () => {
    expect(naturalezaDelCodigo('102.01')).toBe('D');
    expect(naturalezaDelCodigo('201.01')).toBe('A');
    expect(naturalezaDelCodigo('171.05')).toBe('A');
    expect(naturalezaDelCodigo('402.01')).toBe('D');
    expect(naturalezaDelCodigo('801.02')).toBe('A');
    expect(mesSiguiente(2025, 12)).toEqual({ anio: 2026, mes: 1 });
    expect(mesSiguiente(2026, 3)).toEqual({ anio: 2026, mes: 4 });
  });
});
