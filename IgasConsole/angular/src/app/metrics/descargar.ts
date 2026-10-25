/**
 * Guarda un Blob como archivo, disparando la descarga desde el navegador.
 *
 * Hace falta este rodeo porque el endpoint va AUTENTICADO: un `<a href>` normal es una
 * navegación aparte, sin la cabecera Authorization, y devolvería un 401. Así que la
 * respuesta se pide con HttpClient (que sí lleva el token), y el archivo se fabrica en
 * memoria.
 *
 * La alternativa habitual —meter el token en la URL— lo deja escrito en el historial del
 * navegador y en los logs del servidor, así que no.
 */
export function descargarBlob(blob: Blob, nombre: string): void {
  const url = URL.createObjectURL(blob);

  try {
    const enlace = document.createElement('a');
    enlace.href = url;
    enlace.download = nombre;
    // Sin añadirlo al documento, Firefox ignora el click.
    document.body.appendChild(enlace);
    enlace.click();
    enlace.remove();
  } finally {
    // Revocar SIEMPRE: cada createObjectURL retiene el blob en memoria hasta que se libera,
    // y quien exporta suele exportar varias veces seguidas.
    URL.revokeObjectURL(url);
  }
}

/** Nombre con marca de tiempo, para que dos descargas seguidas no se pisen. */
export function nombreConFecha(prefijo: string): string {
  const ahora = new Date();
  const dos = (n: number) => String(n).padStart(2, '0');
  const marca =
    `${ahora.getFullYear()}${dos(ahora.getMonth() + 1)}${dos(ahora.getDate())}` +
    `-${dos(ahora.getHours())}${dos(ahora.getMinutes())}${dos(ahora.getSeconds())}`;
  return `${prefijo}-${marca}.csv`;
}
