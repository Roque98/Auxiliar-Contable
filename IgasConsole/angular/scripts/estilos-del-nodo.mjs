// La pantalla local del nodo (la computadora de la estación) se ve IGUAL que la consola: usa
// las mismas hojas, compiladas de estos mismos .scss. El nodo no puede depender de Node/Angular
// al compilarse, así que el resultado se guarda en el repo junto con fuentes y logos:
//
//   node scripts/estilos-del-nodo.mjs        (desde angular/)
//
// Córrelo cuando cambie el design system, el armazón o las pantallas de Estaciones, y súbelo
// en el mismo commit. Salida: src/IgasConsole.Nodo/Local/web/assets/.
import { copyFileSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import * as sass from 'sass';

const angular = join(dirname(fileURLToPath(import.meta.url)), '..');
const src = join(angular, 'src');
const salida = join(angular, '..', 'src', 'IgasConsole.Nodo', 'Local', 'web', 'assets');
const leer = ruta => readFileSync(join(src, ruta), 'utf8');

// Las piezas, en el orden de la consola. El puente con Bootstrap/ABP no hace falta: aquí no
// hay pantallas de ABP. El armazón es de un componente encapsulado: su `:host` es el bloque.
const pantallas = ['resumen', 'pista', 'tanques', 'recepciones', 'volumetrico', 'nodo'];
const entrada = [
  "@use 'styles/igas/tokens';",
  "@use 'styles/igas/base';",
  "@use 'styles/igas/componentes';",
  "@use 'styles/igas/utilidades';",
  leer('styles.scss').replace(/@use [^;]+;/g, ''),
  leer('app/layout/igas-layout.component.scss').replace(/:host\b/g, 'app-igas-layout'),
  leer('app/estaciones/estaciones.component.scss'),
  ...pantallas.map(p => leer(`app/estaciones/pantallas/${p}.component.scss`)),
].join('\n');

const css = sass.compileString(entrada, { loadPaths: [src], style: 'compressed', silenceDeprecations: ['import'] }).css;

const reboot = readFileSync(join(angular, 'node_modules/bootstrap/dist/css/bootstrap-reboot.min.css'), 'utf8')
  .replace(/\/\*# sourceMappingURL=.*?\*\//, '');
const iconos = readFileSync(join(angular, 'node_modules/bootstrap-icons/font/bootstrap-icons.css'), 'utf8')
  .replace(/url\("?\.\/fonts\/bootstrap-icons\.(woff2?)[^)]*\)/g, (_, ext) => `url("/assets/fonts/bootstrap-icons.${ext}")`);

mkdirSync(join(salida, 'fonts'), { recursive: true });
mkdirSync(join(salida, 'images', 'logo'), { recursive: true });
writeFileSync(
  join(salida, 'consola.css'),
  `/* GENERADO por angular/scripts/estilos-del-nodo.mjs: no editar a mano. */\n${reboot}\n${iconos}\n${css}\n`,
);

for (const f of ['sora-latin.woff2', 'sora-latin-ext.woff2', 'OFL.txt']) {
  copyFileSync(join(src, 'assets/fonts', f), join(salida, 'fonts', f));
}
for (const f of ['bootstrap-icons.woff2', 'bootstrap-icons.woff']) {
  copyFileSync(join(angular, 'node_modules/bootstrap-icons/font/fonts', f), join(salida, 'fonts', f));
}
for (const f of ['zia-isotipo.png', 'zia-isotipo-32.png', 'zia-logotipo.png', 'zia-logotipo-claro.png']) {
  copyFileSync(join(src, 'assets/images/logo', f), join(salida, 'images', 'logo', f));
}
console.log(`Listo: ${salida} (consola.css ${Math.round(css.length / 1024)} KB)`);
