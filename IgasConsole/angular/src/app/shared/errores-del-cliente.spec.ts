import { describe, expect, it } from 'vitest';
import { armarReporte, versionDeLaWeb } from './errores-del-cliente';

describe('armarReporte', () => {
  it('manda el nombre, el mensaje, la pila y dónde pasó', () => {
    const e = new TypeError('no se pudo leer «x»');
    const r = armarReporte(e, '/escritorios', 'ABC123', 'Chrome')!;
    expect(r.origen).toBe('web');
    expect(r.mensaje).toBe('TypeError: no se pudo leer «x»');
    expect(r.pila).toContain('TypeError');
    expect(r.ruta).toBe('/escritorios');
    expect(r.version).toBe('ABC123');
  });

  it('no manda los fallos de carga tras un despliegue ni la falta de red', () => {
    expect(armarReporte(new TypeError('Failed to fetch dynamically imported module: /chunk-X.js'), '/', 'v', 'a')).toBeNull();
    expect(armarReporte({ name: 'HttpErrorResponse', status: 0, message: 'Http failure' }, '/', 'v', 'a')).toBeNull();
  });

  it('desenvuelve las promesas rechazadas y recorta lo largo', () => {
    const r = armarReporte({ rejection: new Error('x'.repeat(5000)) }, '/'.repeat(500), 'v', 'a')!;
    expect(r.mensaje.length).toBe(1000);
    expect(r.ruta!.length).toBe(300);
    expect(armarReporte('texto suelto', '/', 'v', 'a')!.mensaje).toBe('texto suelto');
  });
});

describe('versionDeLaWeb', () => {
  it('sale del hash del main.js cargado', () => {
    // Las pruebas corren en Node (sin DOM): un documento mínimo con lo que se usa.
    const con = (src: string | null) =>
      ({ querySelector: () => (src === null ? null : { getAttribute: () => src }) }) as unknown as Document;
    expect(versionDeLaWeb(con('main-6AJYCX2I.js'))).toBe('6AJYCX2I');
    expect(versionDeLaWeb(con(null))).toBe('desarrollo');
  });
});
