/**
 * La respuesta del asistente en bloques para pintarla ordenada: títulos, párrafos, listas, tablas de cálculo y el
 * fundamento al final. Se interpreta un markdown mínimo y NUNCA se inserta HTML: el componente pinta cada bloque con
 * su plantilla, así que un texto del modelo no puede meter etiquetas ni scripts.
 */
export interface Trozo {
  texto: string;
  negrita: boolean;
}

/** Un solo tipo (no unión) para que la plantilla lo lea sin depender del estrechamiento de tipos de @switch. */
export interface Bloque {
  tipo: 'titulo' | 'parrafo' | 'lista' | 'tabla';
  trozos?: Trozo[];
  fundamento?: boolean;
  ordenada?: boolean;
  items?: Trozo[][];
  encabezado?: Trozo[][];
  filas?: Trozo[][][];
  numericas?: boolean[];
}

/** `**negrita**` dentro de una línea. Lo demás va tal cual. */
export function trozos(linea: string): Trozo[] {
  const fuera: Trozo[] = [];
  const partes = linea.split('**');
  partes.forEach((p, i) => {
    if (p) {
      // Un ** sin cerrar se queda como texto.
      const negrita = i % 2 === 1 && i < partes.length - 1;
      fuera.push({ texto: negrita || i % 2 === 0 ? p : '**' + p, negrita });
    }
  });
  return fuera;
}

const FILA = /^\s*\|.*\|\s*$/;
const SEPARADOR = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/;
const VINETA = /^\s*[-*•]\s+(.*)$/;
const NUMERO = /^\s*\d+[.)]\s+(.*)$/;
const TITULO = /^\s*#{1,4}\s+(.*)$/;
const TITULO_EN_NEGRITA = /^\s*\*\*([^*]{1,80})\*\*:?\s*$/;
const ES_FUNDAMENTO = /^(fundamento|fundamentos|base legal|sustento)\b/i;
const CIFRA = /^[\s$%()+\-−–×x=.,\d]+$/;

function celdas(linea: string): string[] {
  return linea.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(c => c.trim());
}

export function bloquesDeRespuesta(texto: string): Bloque[] {
  const lineas = (texto ?? '').replace(/\r/g, '').split('\n');
  const bloques: Bloque[] = [];
  let parrafo: string[] = [];

  const cerrarParrafo = () => {
    if (parrafo.length) {
      bloques.push({ tipo: 'parrafo', trozos: trozos(parrafo.join('\n')) });
      parrafo = [];
    }
  };

  for (let i = 0; i < lineas.length; i++) {
    const l = lineas[i];
    if (!l.trim()) {
      cerrarParrafo();
      continue;
    }

    // Tabla: encabezado | separador | filas.
    if (FILA.test(l) && i + 1 < lineas.length && SEPARADOR.test(lineas[i + 1])) {
      cerrarParrafo();
      const encabezado = celdas(l);
      const filas: string[][] = [];
      i += 2;
      while (i < lineas.length && FILA.test(lineas[i])) {
        filas.push(celdas(lineas[i]));
        i++;
      }
      i--;
      const numericas = encabezado.map((_, c) => filas.length > 0 && filas.every(f => !f[c] || CIFRA.test(f[c].replace(/\*\*/g, ''))));
      bloques.push({
        tipo: 'tabla',
        encabezado: encabezado.map(trozos),
        filas: filas.map(f => encabezado.map((_, c) => trozos(f[c] ?? ''))),
        numericas,
      });
      continue;
    }

    const titulo = TITULO.exec(l) ?? TITULO_EN_NEGRITA.exec(l);
    if (titulo) {
      cerrarParrafo();
      const t = titulo[1].replace(/\*\*/g, '').trim();
      bloques.push({ tipo: 'titulo', trozos: trozos(t), fundamento: ES_FUNDAMENTO.test(t) });
      continue;
    }

    const vineta = VINETA.exec(l);
    const numero = vineta ? null : NUMERO.exec(l);
    if (vineta || numero) {
      cerrarParrafo();
      const ordenada = !!numero;
      const item = trozos((vineta ?? numero)![1]);
      const ultimo = bloques[bloques.length - 1];
      if (ultimo?.tipo === 'lista' && ultimo.ordenada === ordenada && ultimo.items) {
        ultimo.items.push(item);
      } else {
        bloques.push({ tipo: 'lista', ordenada, items: [item] });
      }
      continue;
    }

    parrafo.push(l);
  }

  cerrarParrafo();
  return bloques;
}
