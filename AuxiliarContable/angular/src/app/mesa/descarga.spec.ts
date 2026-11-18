import { describe, expect, it } from 'vitest';
import { MAX_MESES_POR_ORDEN, diasDeLaOrden, textoDeLaRevision, foliosEn, haceCuanto, mesesDeLaOrden, textoDelPaquete, textoDeLaSolicitud } from './descarga';

describe('orden del Descargador 1', () => {
  it('cuenta los meses de la orden, cruzando el año', () => {
    expect(mesesDeLaOrden(2026, 3, 2026, 3)).toBe(1);
    expect(mesesDeLaOrden(2025, 11, 2026, 2)).toBe(4);
    expect(mesesDeLaOrden(2026, 5, 2026, 2)).toBeLessThan(1);
    expect(mesesDeLaOrden(2024, 1, 2026, 1)).toBeGreaterThan(MAX_MESES_POR_ORDEN);
  });
});

describe('resultado del paquete', () => {
  it('dice lo nuevo, lo repetido, lo cambiado y lo cancelado', () => {
    const texto = textoDelPaquete({
      desde: '2026-01', hasta: '2026-03', nuevas: 12, repetidas: 3, reemplazadas: 1, enMesCerrado: 0, rechazadas: 0,
      estatusPuestos: 15, canceladas: 2, enProceso: 1, sinXmlEnElSat: 0, avisos: [],
    });
    expect(texto).toBe('12 facturas nuevas · 3 ya estaban · 1 XML dañado cambiado por el bueno · estatus del SAT en 15 · 2 canceladas · 1 en proceso de cancelación (2026-01 a 2026-03).');
  });

  it('con un solo mes y sin nada extra', () => {
    const texto = textoDelPaquete({
      desde: '2026-02', hasta: '2026-02', nuevas: 1, repetidas: 0, reemplazadas: 0, enMesCerrado: 0, rechazadas: 0,
      estatusPuestos: 1, canceladas: 0, enProceso: 0, sinXmlEnElSat: 0, avisos: [],
    });
    expect(texto).toBe('1 factura nueva · estatus del SAT en 1 (2026-02).');
  });
});

describe('cuándo se calculó', () => {
  const ahora = new Date('2026-11-17T18:00:00Z');
  it('en minutos u horas', () => {
    expect(haceCuanto('2026-11-17T17:59:30Z', ahora)).toBe('hace un momento');
    expect(haceCuanto('2026-11-17T17:57:00Z', ahora)).toBe('hace 3 min');
    expect(haceCuanto('2026-11-17T15:00:00Z', ahora)).toBe('hace 3 h');
    expect(haceCuanto('2026-11-14T18:00:00Z', ahora)).toMatch(/^el /);
  });
});

describe('orden por días y por folio', () => {
  it('cuenta los días con los dos extremos', () => {
    expect(diasDeLaOrden('2026-11-01', '2026-11-07')).toBe(7);
    expect(diasDeLaOrden('2026-11-05', '2026-11-05')).toBe(1);
    expect(diasDeLaOrden('2026-11-05', '2026-11-01')).toBeLessThan(1);
    expect(diasDeLaOrden('', '2026-11-01')).toBeNull();
  });

  it('saca los UUID de lo pegado, sin repetir', () => {
    const t = 'Factura 6f9619ff-8b86-d011-b42d-00c04fc964ff y la 6F9619FF-8B86-D011-B42D-00C04FC964FF; otra: 11111111-2222-3333-4444-555555555555, basura 1234';
    expect(foliosEn(t)).toEqual(['6F9619FF-8B86-D011-B42D-00C04FC964FF', '11111111-2222-3333-4444-555555555555']);
    expect(foliosEn('')).toEqual([]);
  });
});

describe('revisión de estatus', () => {
  const base = { revisadas: 0, cambiaron: 0, canceladas: 0, enProceso: 0, noEncontradas: 0, fallaron: 0, faltan: 0, satSinResponder: false };
  it('dice qué cambió', () => {
    expect(textoDeLaRevision({ ...base, revisadas: 40, cambiaron: 2, canceladas: 1, enProceso: 1 }))
      .toBe('40 revisadas en el SAT · 2 cambiaron · 1 cancelada · 1 en proceso de cancelación.');
    expect(textoDeLaRevision({ ...base, revisadas: 150, faltan: 30 })).toContain('faltan 30');
  });
  it('si el SAT no respondió', () => {
    expect(textoDeLaRevision({ ...base, fallaron: 5, satSinResponder: true })).toBe('El SAT no respondió: se vuelve a intentar esta noche.');
  });
});

describe('descarga masiva', () => {
  const base = {
    id: 'x', anio: 2026, mes: 10, emitidas: true, desde: '2026-10-01T00:00:00', hasta: '2026-10-31T23:59:59', automatica: false,
    numeroCfdis: 0, paquetes: 0, paquetesBajados: 0, nuevas: 0, repetidas: 0, rechazadas: 0, solicitadaUtc: '2026-11-03T12:00:00Z',
  };

  it('dice cómo va cada solicitud', () => {
    expect(textoDeLaSolicitud({ ...base, estado: 'EnProceso' })).toBe('Emitidas: el SAT la está armando; se baja sola en cuanto esté.');
    expect(textoDeLaSolicitud({ ...base, estado: 'EnProceso', paquetes: 3, paquetesBajados: 1, automatica: true }))
      .toBe('Emitidas (sola, al cerrar el mes): bajando paquetes (1 de 3).');
    expect(textoDeLaSolicitud({ ...base, emitidas: false, estado: 'Descargada', nuevas: 12, repetidas: 3 }))
      .toBe('Recibidas: lista — 12 facturas nuevas · 3 ya estaban.');
    expect(textoDeLaSolicitud({ ...base, estado: 'SinFacturas' })).toBe('Emitidas: el SAT no tiene facturas en ese rango.');
    expect(textoDeLaSolicitud({ ...base, estado: 'Rechazada', mensaje: 'El SAT dice que el certificado no es válido.' }))
      .toBe('Emitidas: no se pudo — El SAT dice que el certificado no es válido.');
  });
});
