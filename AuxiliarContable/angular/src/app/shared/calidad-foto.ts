// Revisión de una foto antes de subirla (ticket, nota, comprobante): si se ve que no se va a poder leer, se rechaza en
// el navegador y se dice por qué, para que la tomen otra vez. No gasta Claude ni sube nada. Lo puro (medir y dictaminar)
// se prueba sin navegador; `revisarFoto` decodifica el archivo con canvas.

/** Lado mayor al que se reduce la foto para medirla: así la nitidez de fotos de distinto tamaño es comparable. */
export const LADO_DE_MEDICION = 1000;

/**
 * Umbrales calibrados con tickets de prueba (nov-2026): con nitidez 455 Claude Haiku lee bien una foto algo
 * desenfocada; por debajo de 30 el texto ya no se distingue. Conservadores a propósito: rechazar una foto buena
 * molesta más que dejar pasar una regular (a esa la revisa después un modelo mejor).
 */
export const UMBRALES = {
  ladoMenorMinimo: 600,
  nitidezMinima: 30,
  brilloMinimo: 55,
  brilloMaximo: 235,
  contrasteMinimo: 18,
};

export interface MedidaDeFoto {
  ancho: number;
  alto: number;
  /** 0-255, promedio de gris. */
  brillo: number;
  /** Desviación estándar del gris: qué tanto se separa el texto del fondo. */
  contraste: number;
  /** Varianza del laplaciano en las zonas más nítidas (percentil 90 de una cuadrícula de 8×8): el fondo no la baja. */
  nitidez: number;
}

export interface DictamenDeFoto {
  ok: boolean;
  motivos: string[];
  /** Lo que se le dice a quien sube la foto. */
  mensaje: string;
}

/** RGBA -> gris (0-255). */
export function aGris(rgba: Uint8ClampedArray | number[], ancho: number, alto: number): Float32Array {
  const g = new Float32Array(ancho * alto);
  for (let i = 0; i < g.length; i++) {
    g[i] = 0.299 * rgba[i * 4] + 0.587 * rgba[i * 4 + 1] + 0.114 * rgba[i * 4 + 2];
  }
  return g;
}

/** Brillo, contraste y nitidez de una imagen en gris ya reducida. `anchoOriginal`/`altoOriginal`: la foto tal cual. */
export function medir(gris: Float32Array, ancho: number, alto: number, anchoOriginal = ancho, altoOriginal = alto): MedidaDeFoto {
  let suma = 0;
  let suma2 = 0;
  for (const v of gris) {
    suma += v;
    suma2 += v * v;
  }
  const n = gris.length || 1;
  const brillo = suma / n;
  const contraste = Math.sqrt(Math.max(0, suma2 / n - brillo * brillo));

  // Laplaciano de 4 vecinos y su varianza por celda de una cuadrícula de 8×8.
  const celdas = 8;
  const s = new Float64Array(celdas * celdas);
  const s2 = new Float64Array(celdas * celdas);
  const c = new Float64Array(celdas * celdas);
  for (let y = 1; y < alto - 1; y++) {
    const cy = Math.min(celdas - 1, Math.floor(((y - 1) * celdas) / Math.max(1, alto - 2)));
    for (let x = 1; x < ancho - 1; x++) {
      const i = y * ancho + x;
      const l = gris[i - 1] + gris[i + 1] + gris[i - ancho] + gris[i + ancho] - 4 * gris[i];
      const k = cy * celdas + Math.min(celdas - 1, Math.floor(((x - 1) * celdas) / Math.max(1, ancho - 2)));
      s[k] += l;
      s2[k] += l * l;
      c[k]++;
    }
  }
  const varianzas: number[] = [];
  for (let k = 0; k < s.length; k++) {
    if (c[k] > 0) {
      const m = s[k] / c[k];
      varianzas.push(s2[k] / c[k] - m * m);
    }
  }
  varianzas.sort((a, b) => a - b);
  const nitidez = varianzas.length ? varianzas[Math.min(varianzas.length - 1, Math.floor(0.9 * (varianzas.length - 1) + 0.5))] : 0;

  return {
    ancho: anchoOriginal,
    alto: altoOriginal,
    brillo: Math.round(brillo),
    contraste: Math.round(contraste),
    nitidez: Math.round(nitidez),
  };
}

/** Si la foto se acepta y, si no, por qué y qué hacer. */
export function dictaminar(m: MedidaDeFoto, u = UMBRALES): DictamenDeFoto {
  const motivos: string[] = [];
  if (Math.min(m.ancho, m.alto) < u.ladoMenorMinimo) {
    motivos.push(`la resolución es muy baja (${m.ancho}×${m.alto}): tómala con la cámara, no una captura reducida o enviada por chat`);
  }
  if (m.brillo < u.brilloMinimo) {
    motivos.push('está muy oscura: tómala con más luz');
  } else if (m.contraste < u.contrasteMinimo) {
    // Una captura de pantalla casi toda blanca tiene buen contraste: solo se rechaza si el texto se pierde.
    motivos.push(
      m.brillo > u.brilloMaximo
        ? 'está deslavada o con reflejo: evita el flash directo y la luz de frente'
        : 'el texto casi no se distingue del fondo',
    );
  }
  if (m.nitidez < u.nitidezMinima) {
    motivos.push('está borrosa o movida: acércate, enfoca tocando el ticket en la pantalla y no muevas el celular');
  }
  const ok = motivos.length === 0;
  return {
    ok,
    motivos,
    mensaje: ok
      ? ''
      : `La foto no se va a poder leer: ${motivos.join('; ')}. Vuelve a tomarla de cerca, con el ticket completo y plano, y súbela otra vez.`,
  };
}

/** ¿Es una foto (y no un PDF o un XML)? */
export function esFoto(archivo: { name: string; type: string }): boolean {
  return archivo.type.startsWith('image/') || /\.(jpe?g|png|webp|heic|heif|bmp|gif)$/i.test(archivo.name);
}

/**
 * Revisa la foto en el navegador. Lo que no es foto, o una foto que el navegador no sabe abrir (p. ej. HEIC en
 * algunos), pasa sin revisar: no se rechaza lo que no se pudo medir.
 */
export async function revisarFoto(archivo: File): Promise<DictamenDeFoto> {
  const pasa: DictamenDeFoto = { ok: true, motivos: [], mensaje: '' };
  if (!esFoto(archivo) || typeof createImageBitmap !== 'function' || typeof document === 'undefined') {
    return pasa;
  }
  let imagen: ImageBitmap;
  try {
    imagen = await createImageBitmap(archivo);
  } catch {
    return pasa;
  }
  try {
    const escala = Math.min(1, LADO_DE_MEDICION / Math.max(imagen.width, imagen.height));
    const ancho = Math.max(3, Math.round(imagen.width * escala));
    const alto = Math.max(3, Math.round(imagen.height * escala));
    const lienzo = document.createElement('canvas');
    lienzo.width = ancho;
    lienzo.height = alto;
    const ctx = lienzo.getContext('2d', { willReadFrequently: true });
    if (!ctx) {
      return pasa;
    }
    ctx.drawImage(imagen, 0, 0, ancho, alto);
    const datos = ctx.getImageData(0, 0, ancho, alto).data;
    return dictaminar(medir(aGris(datos, ancho, alto), ancho, alto, imagen.width, imagen.height));
  } catch {
    return pasa;
  } finally {
    imagen.close();
  }
}
