/**
 * Reescala una imagen en el NAVEGADOR antes de subirla.
 *
 * Por qué aquí y no en el servidor: una foto de móvil ronda los 5 MB y, como avatar, se ve
 * a 40 píxeles. Reescalar en el cliente evita subir esos 5 MB por una conexión que puede
 * ser móvil, y evita además meter una librería de imágenes en el backend.
 *
 * Esto es COMODIDAD, no seguridad: cualquiera puede llamar a la API sin pasar por esta
 * pantalla. El límite que de verdad protege es el del servidor (ver ProfilePictureConsts).
 *
 * La salida siempre es JPEG sobre fondo blanco. El formato de origen se pierde a
 * propósito: un PNG con transparencia quedaría con el fondo negro al pasarlo a JPEG, y un
 * avatar no necesita canal alfa. A cambio, el resultado es predecible y pequeño.
 */
export async function normalizarImagen(archivo: File, ladoMaximo: number): Promise<Blob> {
  // imageOrientation: 'from-image' aplica la rotación EXIF. Sin esto, las fotos hechas con
  // el móvil en vertical se suben tumbadas: el sensor las guarda apaisadas y la orientación
  // real va en los metadatos, que el canvas ignora por defecto.
  const bitmap = await createImageBitmap(archivo, { imageOrientation: 'from-image' });

  try {
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

    // Fondo blanco explícito: el canvas nace transparente y el JPEG no guarda
    // transparencia, así que sin esto los PNG recortados salen con manchas negras.
    contexto.fillStyle = '#ffffff';
    contexto.fillRect(0, 0, ancho, alto);
    contexto.drawImage(bitmap, 0, 0, ancho, alto);

    return await new Promise<Blob>((resolver, rechazar) => {
      lienzo.toBlob(
        blob => (blob ? resolver(blob) : rechazar(new Error('No se pudo convertir la imagen.'))),
        'image/jpeg',
        0.85,
      );
    });
  } finally {
    bitmap.close();
  }
}
