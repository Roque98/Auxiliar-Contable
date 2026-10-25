import { describe, expect, it } from 'vitest';
import { TipoDeDocumento } from '../documentos/tipos';
import { tipoProbable } from './adjuntos';

const archivo = (nombre: string, tipo = '') => new File(['x'], nombre, { type: tipo });

describe('tipoProbable (qué es lo que adjuntaron al chat)', () => {
  it('un XML siempre es factura', () => {
    expect(tipoProbable(archivo('A1B2-constancia.XML', 'text/xml'))).toBe(TipoDeDocumento.Cfdi);
  });

  it('la constancia se reconoce por su nombre y va al perfil', () => {
    expect(tipoProbable(archivo('CSF_ABC010101XYZ.pdf', 'application/pdf'))).toBe('csf');
    expect(tipoProbable(archivo('Constancia de situación fiscal.pdf', 'application/pdf'))).toBe('csf');
  });

  it('el estado de cuenta se reconoce por su nombre o el banco', () => {
    expect(tipoProbable(archivo('EdoCta_octubre.pdf', 'application/pdf'))).toBe(TipoDeDocumento.EstadoDeCuenta);
    expect(tipoProbable(archivo('BBVA 2026-10.pdf', 'application/pdf'))).toBe(TipoDeDocumento.EstadoDeCuenta);
  });

  it('una foto sin más pistas es ticket; un PDF sin pistas, estado de cuenta', () => {
    expect(tipoProbable(archivo('IMG_2041.jpg', 'image/jpeg'))).toBe(TipoDeDocumento.Ticket);
    expect(tipoProbable(archivo('documento.pdf', 'application/pdf'))).toBe(TipoDeDocumento.EstadoDeCuenta);
  });
});
