import { defineConfig } from 'vitest/config';

/**
 * Pruebas de la LÓGICA PURA del front (`npm run test:unit`).
 *
 * ⚠️⚠️ **Por qué no se usa `ng test`.** El arrancador de pruebas de Angular
 * (`@angular/build:unit-test`) emite `await import('zone.js/testing')` en el nivel superior
 * del módulo, y esta aplicación usa **zone.js** — como debe (§5.1: media docena de pantallas
 * dependen de `NgZone.run(...)` para repintarse). Al detectar zone.js, Angular le pide a
 * esbuild que degrade `async/await`:
 *
 *     // node_modules/@angular/build/src/tools/esbuild/utils.js
 *     // "Native async/await is not supported with Zone.js."
 *     'async-await': nativeAsyncAwait,
 *
 * y un `await` de nivel superior **no se puede degradar**, así que el build de test falla
 * entero antes de ejecutar una sola prueba:
 *
 *     ERROR: Top-level await is not available in the configured target environment
 *
 * No es configuración mal puesta: es una incompatibilidad entre ese runner y zone.js. Se
 * llegó a ella leyendo el código de Angular, después de descartar browserslist (que sí era
 * un problema aparte y está arreglado en `.browserslistrc`).
 *
 * **Lo que sí se puede probar, y es lo que vale.** Los fallos de front que ha tenido este
 * proyecto —callbacks fuera de la zona, variables CSS del tema, tablas que se salen en
 * móvil— **no los caza ningún test unitario**: viven en un navegador de verdad y se han
 * encontrado automatizándolo. Lo que sí se rompe en silencio y aquí queda cubierto son las
 * **funciones puras que están escritas dos veces**, en TypeScript y en C#: la forma de las
 * URLs de los espacios y la del logo. Ahí un carácter de diferencia deja a un inquilino sin
 * poder iniciar sesión, y hasta ahora solo la mitad de C# tenía tests.
 *
 * Por eso el entorno es `node` y no un navegador: estas pruebas no tocan el DOM ni Angular.
 * El día que haga falta probar un componente, esto se queda corto y habrá que volver a la
 * pelea de arriba (o ir a zoneless, que es un cambio de otra escala).
 */
export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.spec.ts'],
    globals: false,
    reporters: ['default'],
  },
});
