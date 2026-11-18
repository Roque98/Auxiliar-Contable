import type { ResultadoDelPaqueteDto } from './descarga-sat.service';

/** Meses que abarca una orden del Descargador 1 (0 o menos = el final es antes del inicio). */
export function mesesDeLaOrden(desdeAnio: number, desdeMes: number, hastaAnio: number, hastaMes: number): number {
  return (hastaAnio - desdeAnio) * 12 + hastaMes - desdeMes + 1;
}

/** El tope que pone la plataforma (la sesión del SAT caduca si se piden demasiados meses). */
export const MAX_MESES_POR_ORDEN = 24;

/** Lo que pasó al subir un paquete, en una frase. */
export function textoDelPaquete(r: ResultadoDelPaqueteDto): string {
  const partes = [`${r.nuevas} ${r.nuevas === 1 ? 'factura nueva' : 'facturas nuevas'}`];
  if (r.repetidas) {
    partes.push(`${r.repetidas} ya estaban`);
  }
  if (r.reemplazadas) {
    partes.push(`${r.reemplazadas} ${r.reemplazadas === 1 ? 'XML dañado cambiado' : 'XML dañados cambiados'} por el bueno`);
  }
  partes.push(`estatus del SAT en ${r.estatusPuestos}`);
  if (r.canceladas) {
    partes.push(`${r.canceladas} ${r.canceladas === 1 ? 'cancelada' : 'canceladas'}`);
  }
  const periodo = r.desde && r.hasta ? (r.desde === r.hasta ? ` (${r.desde})` : ` (${r.desde} a ${r.hasta})`) : '';
  return partes.join(' · ') + periodo + '.';
}

/** Un archivo que viene en base64 del servidor, como descarga del navegador. */
export function descargarBase64(nombre: string, contenidoB64: string, tipo: string): void {
  const bytes = Uint8Array.from(atob(contenidoB64), c => c.charCodeAt(0));
  descargarBlob(nombre, new Blob([bytes], { type: tipo }));
}

export function descargarBlob(nombre: string, blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = nombre;
  enlace.click();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** «hace 3 min», «hace 2 h», «el 14 nov»: cuándo se calculó, para ver de un vistazo si está al día. */
export function haceCuanto(utc: string, ahora: Date = new Date()): string {
  const fecha = new Date(utc);
  const segundos = Math.max(0, Math.round((ahora.getTime() - fecha.getTime()) / 1000));
  if (segundos < 60) {
    return 'hace un momento';
  }
  const minutos = Math.round(segundos / 60);
  if (minutos < 60) {
    return `hace ${minutos} min`;
  }
  const horas = Math.round(minutos / 60);
  if (horas < 24) {
    return `hace ${horas} h`;
  }
  return 'el ' + fecha.toLocaleDateString('es-MX', { day: 'numeric', month: 'short' });
}
