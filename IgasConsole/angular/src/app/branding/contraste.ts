/**
 * Contraste de color, según WCAG. **Sin una sola dependencia de Angular**, y eso es
 * deliberado.
 *
 * <p>⚠️ Vivía dentro de `branding.provider.ts`, y desde allí **no se podía probar**: ese
 * archivo arrastra la inyección de dependencias de Angular, así que importarlo desde un test
 * de Node revienta pidiendo el compilador. Es la misma historia que `ResolutorDeClaveDeApi`
 * en el servidor — la decisión más delicada acababa siendo la única fuera del alcance de los
 * tests solo por dónde estaba escrita.</p>
 *
 * <p>Aquí vive el criterio; en `branding.provider.ts` queda el cableado (leer la marca,
 * escribir las variables CSS).</p>
 */

/** Contraste mínimo de WCAG AA para texto normal. */
export const AA = 4.5;

export const TEXTO_OSCURO = '#212529';
export const TEXTO_CLARO = '#ffffff';

/** Luminancia relativa de un `#rrggbb`, según la fórmula de WCAG. */
export function luminancia(color: string): number {
  const n = parseInt(color.slice(1), 16);
  const canal = (c: number) => {
    const x = c / 255;
    return x <= 0.03928 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4);
  };
  return (
    0.2126 * canal((n >> 16) & 255) +
    0.7152 * canal((n >> 8) & 255) +
    0.0722 * canal(n & 255)
  );
}

/** Relación de contraste entre dos `#rrggbb`. 1 = idénticos, 21 = negro sobre blanco. */
export function contraste(a: string, b: string): number {
  const [x, y] = [luminancia(a), luminancia(b)];
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
}

/** El color multiplicado por un factor hacia negro. `1` lo deja igual. */
function oscurecer(color: string, factor: number): string {
  const n = parseInt(color.slice(1), 16);
  const c = (v: number) => Math.max(0, Math.min(255, Math.round(v * factor)));
  const canales = [c(((n >> 16) & 255) * 1), c(((n >> 8) & 255) * 1), c((n & 255) * 1)];
  return '#' + canales.map(v => v.toString(16).padStart(2, '0')).join('');
}

/**
 * El acento del inquilino preparado para **llevar texto encima**: qué color de relleno usar
 * y de qué color va ese texto.
 *
 * <p>⚠️⚠️ <b>Por qué hace falta esto y no vale el acento a secas.</b> Un botón primario es el
 * acento del inquilino con texto dentro, y <b>el color lo elige el inquilino</b>. Medido con
 * el que hay puesto hoy (<c>#f72585</c>): blanco encima da <b>3,78</b> y texto oscuro
 * <b>4,08</b> — o sea que <b>ninguna de las dos opciones llega al 4,5 que exige WCAG AA</b>.
 * Lo detectó axe-core sobre las pantallas reales; no es una preferencia estética, es texto
 * que cuesta leer.</p>
 *
 * <p>No se puede resolver con un color fijo en el CSS, porque el color no lo elegimos
 * nosotros: lo que sirve para un rosa no sirve para un amarillo claro ni para un azul
 * marino.</p>
 *
 * <p><b>El criterio, en orden:</b></p>
 * <ol>
 *   <li>Si el acento tal cual ya funciona con alguno de los dos textos, <b>se deja
 *   intacto</b>. La marca del cliente manda mientras se pueda leer.</li>
 *   <li>Si no, se <b>oscurece lo justo</b> hasta que el blanco llegue a AA. Se oscurece y no
 *   se aclara porque el fondo de la aplicación es claro: un acento aclarado se perdería
 *   contra él, y además el resultado sigue reconociéndose como el mismo color.</li>
 * </ol>
 *
 * <p>Solo afecta a las superficies <b>rellenas</b>. Bordes, enlaces, el aro de foco y la barra
 * lateral siguen usando el acento tal cual: ahí no carga texto y la marca se ve exactamente
 * como la eligió su dueño.</p>
 */
export function relleno(color: string | null | undefined): { fondo: string; texto: string } | null {
  if (!color || !/^#[0-9a-fA-F]{6}$/.test(color)) {
    return null;
  }

  const normalizado = color.toLowerCase();

  const claro = contraste(TEXTO_CLARO, normalizado);
  const oscuro = contraste(TEXTO_OSCURO, normalizado);

  if (claro >= AA || oscuro >= AA) {
    return { fondo: normalizado, texto: claro >= oscuro ? TEXTO_CLARO : TEXTO_OSCURO };
  }

  // Se baja de 2 en 2 % para no pasarse: con un paso grande, un color que necesitaba poco
  // acabaría casi negro y la marca dejaría de reconocerse.
  for (let factor = 0.98; factor >= 0.2; factor -= 0.02) {
    const candidato = oscurecer(normalizado, factor);
    if (contraste(TEXTO_CLARO, candidato) >= AA) {
      return { fondo: candidato, texto: TEXTO_CLARO };
    }
  }

  // Un color tan claro que ni oscureciéndolo del todo llegara (no debería ocurrir: el negro
  // da 21). Red de seguridad para no devolver algo ilegible.
  return { fondo: '#000000', texto: TEXTO_CLARO };
}

/** La superficie de contenido de la aplicación: el fondo contra el que se lee casi todo. */
const FONDO_CLARO = '#f5f5f7';

/**
 * El acento del inquilino usado **como color de TEXTO** sobre el fondo claro — enlaces del
 * tipo «ver todo», etiquetas destacadas.
 *
 * <p>Es el problema simétrico de `relleno()` y hace falta por separado: allí el acento es el
 * fondo y el texto va encima; aquí el acento <b>es</b> el texto. Medido con el acento actual:
 * `#f72585` sobre `#f5f5f7` da <b>3,46</b>, y en negrita pequeña, que es como se usa.</p>
 *
 * <p>Se oscurece lo mínimo hasta AA contra el fondo claro. Si ya cumple, se devuelve intacto.</p>
 */
export function acentoParaTexto(color: string | null | undefined): string | null {
  if (!color || !/^#[0-9a-fA-F]{6}$/.test(color)) {
    return null;
  }

  const normalizado = color.toLowerCase();

  if (contraste(normalizado, FONDO_CLARO) >= AA) {
    return normalizado;
  }

  for (let factor = 0.98; factor >= 0.2; factor -= 0.02) {
    const candidato = oscurecer(normalizado, factor);
    if (contraste(candidato, FONDO_CLARO) >= AA) {
      return candidato;
    }
  }

  return '#000000';
}
