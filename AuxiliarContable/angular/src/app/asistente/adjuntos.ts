import { TipoDeDocumento } from '../documentos/tipos';

/** Lo que el chat sabe subir. La constancia va al perfil (se lee y llena datos); lo demás, a la bóveda. */
export type TipoParaAdjuntar = TipoDeDocumento | 'csf';

export const TIPOS_PARA_ADJUNTAR: { valor: TipoParaAdjuntar; nombre: string }[] = [
  { valor: TipoDeDocumento.Cfdi, nombre: 'Factura (XML)' },
  { valor: TipoDeDocumento.EstadoDeCuenta, nombre: 'Estado de cuenta' },
  { valor: 'csf', nombre: 'Constancia fiscal' },
  { valor: TipoDeDocumento.Ticket, nombre: 'Ticket' },
  { valor: TipoDeDocumento.Otro, nombre: 'Otro' },
];

/** Adivina qué es un archivo por su extensión y nombre; el usuario lo corrige con un toque. */
export function tipoProbable(archivo: File): TipoParaAdjuntar {
  const nombre = archivo.name.toLowerCase();
  if (nombre.endsWith('.xml')) {
    return TipoDeDocumento.Cfdi;
  }
  if (/constancia|csf|situaci[oó]n.?fiscal/.test(nombre)) {
    return 'csf';
  }
  if (/estado|edo|cuenta|banc|bbva|banorte|santander|hsbc|banamex|scotia/.test(nombre)) {
    return TipoDeDocumento.EstadoDeCuenta;
  }
  if (archivo.type.startsWith('image/')) {
    return TipoDeDocumento.Ticket;
  }
  return TipoDeDocumento.EstadoDeCuenta;
}
