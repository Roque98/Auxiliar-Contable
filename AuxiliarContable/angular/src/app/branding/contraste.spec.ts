import { describe, expect, it } from 'vitest';
import { contraste, luminancia, relleno } from './contraste';

/**
 * El acento del inquilino preparado para llevar texto encima.
 *
 * ⚠️ **Por qué esto tiene tests y el resto de la piel no.** Casi toda la piel son reglas CSS
 * que se comprueban midiendo en el navegador (así se encontró todo esto, con axe-core). Pero
 * `relleno()` no es CSS: es una **decisión que toma código** sobre un color que elige otra
 * persona, y su resultado —si un botón se lee o no— no se puede revisar mirando el repositorio
 * porque el color no está aquí. La única forma de fijar que la regla sigue valiendo para
 * CUALQUIER color es esta.
 *
 * El caso que lo motivó es real: el acento en producción es `#f72585`, y con él **ni el blanco
 * (3,78) ni el texto oscuro (4,08) llegan al 4,5 de WCAG AA**. Un color fijo en el CSS no
 * podía resolverlo.
 */
describe('el acento cuando lleva texto encima', () => {
  const AA = 4.5;

  it('la fórmula de contraste es la de WCAG', () => {
    // Los dos extremos conocidos: si estos fallan, todo lo demás es ruido.
    expect(contraste('#000000', '#ffffff')).toBeCloseTo(21, 1);
    expect(contraste('#ffffff', '#ffffff')).toBeCloseTo(1, 5);
    // Y el orden no importa.
    expect(contraste('#000000', '#ffffff')).toBeCloseTo(contraste('#ffffff', '#000000'), 5);
  });

  it('la luminancia respeta el peso del verde', () => {
    // WCAG pondera 0.7152 el verde y 0.0722 el azul: un verde puro es mucho más luminoso
    // que un azul puro. Es lo que hace que un badge verde claro sea ilegible con blanco.
    expect(luminancia('#00ff00')).toBeGreaterThan(luminancia('#0000ff'));
    expect(luminancia('#ffffff')).toBeCloseTo(1, 3);
    expect(luminancia('#000000')).toBeCloseTo(0, 5);
  });

  it('⚠️ EL CASO QUE LO MOTIVÓ: el acento de producción no valía con ningún texto', () => {
    const rosa = '#f72585';

    // Se deja constancia del problema, no solo de la solución: si algún día estos números
    // cambian, es que cambió la fórmula y este archivo entero deja de significar lo mismo.
    expect(contraste('#ffffff', rosa)).toBeLessThan(AA);
    expect(contraste('#212529', rosa)).toBeLessThan(AA);

    const r = relleno(rosa)!;
    expect(contraste(r.texto, r.fondo)).toBeGreaterThanOrEqual(AA);
  });

  it('si el color ya vale, NO se toca', () => {
    // La marca del cliente manda mientras se pueda leer. Un azul oscuro funciona con blanco
    // tal cual, y oscurecerlo «por si acaso» sería cambiarle el color a alguien sin motivo.
    const azul = '#0d47a1';
    expect(contraste('#ffffff', azul)).toBeGreaterThanOrEqual(AA);

    const r = relleno(azul)!;
    expect(r.fondo).toBe(azul);
    expect(r.texto).toBe('#ffffff');
  });

  it('un color claro que vale con texto oscuro usa texto oscuro, sin oscurecer el fondo', () => {
    // Un amarillo se lee perfectamente con texto oscuro. Oscurecerlo hasta que valga el
    // blanco lo convertiría en marrón — o sea, en otro color.
    const amarillo = '#ffd400';
    const r = relleno(amarillo)!;

    expect(r.fondo).toBe(amarillo);
    expect(r.texto).toBe('#212529');
    expect(contraste(r.texto, r.fondo)).toBeGreaterThanOrEqual(AA);
  });

  /**
   * La propiedad que de verdad importa: **para cualquier color, el resultado se lee**. Los
   * casos de arriba cubren los que se pensaron; este cubre los que no.
   */
  it('SIEMPRE devuelve una combinación legible, sea cual sea el color', () => {
    const colores: string[] = [];
    for (let r = 0; r <= 255; r += 51) {
      for (let g = 0; g <= 255; g += 51) {
        for (let b = 0; b <= 255; b += 51) {
          colores.push('#' + [r, g, b].map(v => v.toString(16).padStart(2, '0')).join(''));
        }
      }
    }

    const malos = colores.filter(c => {
      const x = relleno(c);
      return !x || contraste(x.texto, x.fondo) < AA;
    });

    expect(malos).toEqual([]);
    expect(colores.length).toBe(216); // que la rejilla se recorrió de verdad
  });

  it('oscurece lo MÍNIMO: el resultado sigue siendo reconocible', () => {
    // Bajar de golpe a negro cumpliría AA y sería inútil: el inquilino dejaría de ver su
    // color. Se comprueba que el resultado conserva el tono — el canal dominante del rosa
    // sigue siendo el rojo, y sigue siendo claramente rosa y no gris.
    const r = relleno('#f72585')!;
    const n = parseInt(r.fondo.slice(1), 16);
    const [rojo, verde, azul] = [(n >> 16) & 255, (n >> 8) & 255, n & 255];

    expect(rojo).toBeGreaterThan(verde);
    expect(rojo).toBeGreaterThan(azul);
    expect(azul).toBeGreaterThan(verde);
    // Y no se ha ido a negro: conserva buena parte de su intensidad.
    expect(rojo).toBeGreaterThan(150);
  });

  it('un color inválido no devuelve nada, y entonces la variable no se fija', () => {
    // Vale más quedarse con el respaldo del CSS que escribir una variable a medias.
    for (const malo of [null, undefined, '', 'rojo', '#fff', '#12345g', 'red } body {']) {
      expect(relleno(malo as string | null)).toBeNull();
    }
  });
});
