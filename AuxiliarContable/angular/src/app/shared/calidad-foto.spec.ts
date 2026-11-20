import { describe, expect, it } from 'vitest';
import { dictaminar, esFoto, medir } from './calidad-foto';

/** Un «ticket» sintético: fondo claro con renglones de texto (barras oscuras con huecos), opcionalmente desenfocado. */
function ticket(ancho: number, alto: number, desenfoque = 0, fondo = 235, tinta = 30): Float32Array {
  let g = new Float32Array(ancho * alto).fill(fondo);
  for (let y = 20; y < alto - 20; y += 16) {
    for (let x = 20; x < ancho - 20; x++) {
      if (x % 7 < 4) {
        for (let k = 0; k < 6; k++) {
          g[(y + k) * ancho + x] = tinta;
        }
      }
    }
  }
  for (let paso = 0; paso < desenfoque; paso++) {
    const h = new Float32Array(g.length);
    for (let y = 1; y < alto - 1; y++) {
      for (let x = 1; x < ancho - 1; x++) {
        let s = 0;
        for (let dy = -1; dy <= 1; dy++) {
          for (let dx = -1; dx <= 1; dx++) {
            s += g[(y + dy) * ancho + x + dx];
          }
        }
        h[y * ancho + x] = s / 9;
      }
    }
    g = h;
  }
  return g;
}

describe('calidad de la foto', () => {
  it('acepta un ticket nítido de buena resolución', () => {
    const d = dictaminar(medir(ticket(300, 400), 300, 400, 3000, 4000));
    expect(d.ok).toBe(true);
    expect(d.mensaje).toBe('');
  });

  it('rechaza una foto borrosa y dice qué hacer', () => {
    const m = medir(ticket(300, 400, 12), 300, 400, 3000, 4000);
    const d = dictaminar(m);
    expect(d.ok).toBe(false);
    expect(d.mensaje).toContain('borrosa');
    expect(d.mensaje).toContain('súbela otra vez');
  });

  it('rechaza una imagen de muy baja resolución', () => {
    const d = dictaminar(medir(ticket(300, 400), 300, 400, 320, 480));
    expect(d.ok).toBe(false);
    expect(d.motivos[0]).toContain('resolución');
  });

  it('rechaza una foto muy oscura y una sin contraste', () => {
    expect(dictaminar(medir(ticket(300, 400, 0, 40, 10), 300, 400, 3000, 4000)).mensaje).toContain('oscura');
    expect(dictaminar(medir(ticket(300, 400, 0, 250, 240), 300, 400, 3000, 4000)).mensaje).toContain('deslavada');
  });

  it('no rechaza una captura de pantalla casi blanca con texto negro', () => {
    const d = dictaminar(medir(ticket(300, 400, 0, 252, 0), 300, 400, 1080, 2340));
    expect(d.ok).toBe(true);
  });

  it('solo revisa fotos', () => {
    expect(esFoto({ name: 'ticket.JPG', type: '' })).toBe(true);
    expect(esFoto({ name: 'x.bin', type: 'image/png' })).toBe(true);
    expect(esFoto({ name: 'estado.pdf', type: 'application/pdf' })).toBe(false);
    expect(esFoto({ name: 'f.xml', type: 'text/xml' })).toBe(false);
  });
});
