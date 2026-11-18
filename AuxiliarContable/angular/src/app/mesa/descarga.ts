import type { ResultadoDelPaqueteDto, RevisionDeEstatusDto, SolicitudDeDescargaMasivaDto } from './descarga-sat.service';

/** Meses que abarca una orden del Descargador 1 (0 o menos = el final es antes del inicio). */
export function mesesDeLaOrden(desdeAnio: number, desdeMes: number, hastaAnio: number, hastaMes: number): number {
  return (hastaAnio - desdeAnio) * 12 + hastaMes - desdeMes + 1;
}

/** Los topes que pone la plataforma (la sesión del SAT caduca si se pide demasiado de una vez). */
export const MAX_MESES_POR_ORDEN = 24;
export const MAX_DIAS_POR_ORDEN = 366;
export const MAX_FOLIOS_POR_ORDEN = 500;

/** Días de una orden por días, contando los dos extremos; null si falta una fecha. */
export function diasDeLaOrden(desde: string, hasta: string): number | null {
  if (!desde || !hasta) {
    return null;
  }
  return Math.round((Date.parse(hasta + 'T00:00:00Z') - Date.parse(desde + 'T00:00:00Z')) / 86_400_000) + 1;
}

/** Los folios fiscales (UUID) de un texto pegado, en mayúsculas y sin repetir. */
export function foliosEn(texto: string): string[] {
  const encontrados = (texto ?? '').match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi) ?? [];
  return [...new Set(encontrados.map(u => u.toUpperCase()))];
}

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
  if (r.enProceso) {
    partes.push(`${r.enProceso} en proceso de cancelación`);
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

/** Lo que dijo el SAT al revisar el estatus, en una frase. */
export function textoDeLaRevision(r: RevisionDeEstatusDto): string {
  if (r.satSinResponder && !r.revisadas) {
    return 'El SAT no respondió: se vuelve a intentar esta noche.';
  }
  const partes = [`${r.revisadas} revisadas en el SAT`];
  partes.push(r.cambiaron ? `${r.cambiaron} cambiaron` : 'ninguna cambió');
  if (r.canceladas) {
    partes.push(`${r.canceladas} ${r.canceladas === 1 ? 'cancelada' : 'canceladas'}`);
  }
  if (r.enProceso) {
    partes.push(`${r.enProceso} en proceso de cancelación`);
  }
  if (r.noEncontradas) {
    partes.push(`${r.noEncontradas} que el SAT no encuentra`);
  }
  if (r.faltan) {
    partes.push(`faltan ${r.faltan} (vuelve a pulsar o se terminan esta noche)`);
  }
  return partes.join(' · ') + '.';
}

/** Cómo va una solicitud de Descarga Masiva, en una línea. */
export function textoDeLaSolicitud(s: SolicitudDeDescargaMasivaDto): string {
  const que = s.emitidas ? 'Emitidas' : 'Recibidas';
  const quien = s.automatica ? ' (sola, al cerrar el mes)' : '';
  switch (s.estado) {
    case 'EnProceso':
      return s.paquetes > 0
        ? `${que}${quien}: bajando paquetes (${s.paquetesBajados} de ${s.paquetes}).`
        : `${que}${quien}: el SAT la está armando; se baja sola en cuanto esté.`;
    case 'Descargada': {
      const partes = [s.nuevas === 1 ? '1 factura nueva' : `${s.nuevas} facturas nuevas`];
      if (s.repetidas) {
        partes.push(`${s.repetidas} ya estaban`);
      }
      if (s.rechazadas) {
        partes.push(`${s.rechazadas} no eran del cliente o no abrieron`);
      }
      return `${que}${quien}: lista — ${partes.join(' · ')}.`;
    }
    case 'SinFacturas':
      return `${que}${quien}: el SAT no tiene facturas en ese rango.`;
    default:
      return `${que}${quien}: no se pudo — ${s.mensaje ?? 'el SAT la rechazó'}`;
  }
}
