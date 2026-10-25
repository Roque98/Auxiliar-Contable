/**
 * Reescala un logo en el NAVEGADOR antes de subirlo.
 *
 * Es primo de `normalizar-imagen.ts` (la foto de perfil) pero **no el mismo**, y la
 * diferencia es justo la que importa: aquel aplana a **JPEG sobre fondo blanco**, y un logo
 * aplanado así sale con un rectángulo blanco alrededor que se ve fatal sobre la barra
 * lateral oscura. Aquí la salida es **PNG y conserva la transparencia**.
 *
 * El precio es un archivo mayor que el JPEG equivalente; por eso el tope del servidor es más
 * alto que el de las fotos de perfil. Sigue siendo comodidad y no seguridad: quien llame a
 * la API directamente se salta esta pantalla, y el límite que protege es el del servidor.
 *
 * Tampoco se aplica rotación EXIF, al revés que en las fotos: un logo no sale de la cámara
 * de un móvil.
 */
export async function normalizarLogo(archivo: File, ladoMaximo: number): Promise<Blob> {
  const bitmap = await createImageBitmap(archivo);

  try {
    // Solo se REDUCE. Ampliar un logo pequeño no añade detalle: lo deja borroso y además
    // pesando más que el original.
    const escala = Math.min(1, ladoMaximo / Math.max(bitmap.width, bitmap.height));
    const ancho = Math.max(1, Math.round(bitmap.width * escala));
    const alto = Math.max(1, Math.round(bitmap.height * escala));

    const lienzo = document.createElement('canvas');
    lienzo.width = ancho;
    lienzo.height = alto;

    const contexto = lienzo.getContext('2d');
    if (!contexto) {
      throw new Error('El navegador no permitió dibujar la imagen.');
    }

    // Sin fillRect: el lienzo nace transparente y así se queda, que es lo que se quiere.
    contexto.drawImage(bitmap, 0, 0, ancho, alto);

    return await new Promise<Blob>((resolver, rechazar) => {
      lienzo.toBlob(
        blob => (blob ? resolver(blob) : rechazar(new Error('No se pudo convertir la imagen.'))),
        'image/png',
      );
    });
  } finally {
    bitmap.close();
  }
}
