// Sin dependencias de Angular, para que las pruebas unitarias (vitest sin compilador) lo importen.

/** Igual que `TipoDeDocumento` en el backend. */
export enum TipoDeDocumento {
  Otro = 0,
  ConstanciaDeSituacionFiscal = 1,
  Cfdi = 2,
  EstadoDeCuenta = 3,
  Ticket = 4,
  ComprobanteDePago = 5,
  ControlInterno = 6,
  Nomina = 7,
  /** Catálogo de cuentas propio del cliente (XML del Anexo 24). */
  CatalogoDeCuentas = 8,
  /** Balanza de comprobación con la que llega el cliente (XML del Anexo 24). */
  BalanzaDeComprobacion = 9,
  /** Una ley estatal o un reglamento municipal (va a «Mis leyes»). */
  LeyLocal = 10,
}
