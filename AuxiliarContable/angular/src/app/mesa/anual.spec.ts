import { describe, expect, it } from 'vitest';
import { aNumero } from './anual';

describe('aNumero (captura de la anual)', () => {
  it('vacío es «no se capturó» y no cero', () => {
    expect(aNumero('')).toBeNull();
    expect(aNumero('  ')).toBeNull();
    expect(aNumero(undefined)).toBeNull();
  });

  it('acepta pesos con signo y comas de miles', () => {
    expect(aNumero('$1,234.50')).toBe(1234.5);
    expect(aNumero('0')).toBe(0);
  });

  it('lo que no es número no se manda', () => {
    expect(aNumero('mucho')).toBeNull();
  });
});
