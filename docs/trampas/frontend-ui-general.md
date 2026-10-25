# Angular: menú/tablas en móvil y traducción de pantallas

> Parte de `CLAUDE.md` — extraído para no cargar todo el historial de trampas en cada sesión. Ver el índice en la raíz.

## 6.9 Angular: menú y tablas en móvil

- **El menú va agrupado en dos ramas** (`route.provider.ts`): *Mi cuenta* (lo que cada
  usuario gestiona de sí mismo) y *Sistema* (lo que se administra de la instalación), más
  la *Administración* que trae ABP. Un padre se declara con `path: undefined` y los hijos
  lo referencian por `parentName`, igual que hacen los módulos del propio ABP.
  - ⚠️ Si los hijos se ocultan a mano con `invisible` (los que no llevan `requiredPolicy`),
    **hay que ocultar también al padre**: si no, un anónimo ve el grupo abriendo un submenú
    vacío. Los hijos con `requiredPolicy` no necesitan nada: ABP esconde el padre solo.
- ⚠️ **Una tabla de seis columnas no cabe en 375 px, y no aparece scroll horizontal**: la
  mitad derecha queda fuera de la pantalla y es **imposible de alcanzar con el dedo**.
  Medido: 656 px de tabla en un viewport de 375. Es un fallo silencioso — en escritorio se
  ve perfecta.
  - Solución: la utilidad global **`.tabla-apilable`** (`styles.scss`). Debajo de 768 px cada
    fila pasa a ser una ficha con la etiqueta de la columna delante. Uso:
    `class="table tabla-apilable"` y un `data-label` en cada `<td>`.
  - Se descartó el scroll horizontal dentro de un contenedor **como solución para móvil**:
    funciona, pero leer seis columnas arrastrando de lado es incómodo. Sí vale como red de
    seguridad **en escritorio**, donde la tabla ya se ve entera y el contenedor solo entra
    en juego si la ventana se estrecha (ver `.tabla-scroll` en `/jobs`). Las dos cosas
    conviven: por debajo de 768 px manda `.tabla-apilable` y el contenedor se desactiva.
  - ⚠️ Cuidado con `display: flex` en un `<td>`: la celda deja de participar en el reparto
    de anchos de la tabla, se queda estrecha y su contenido se apila en vertical. Pasó con
    tres botones de acción en `/jobs`, que estiraban cada fila a tres líneas. La celda se
    deja como celda (`white-space: nowrap` y `width: 1%`).
  - ⚠️ Y cuidado con lo contrario: poner `nowrap` a una fecha larga (`toLocaleString()`
    completo) ensancha la tabla lo justo para que la **página** empiece a desplazarse de
    lado en escritorio. En un listado los segundos no dicen nada — `dateStyle: 'short',
    timeStyle: 'short'` cabe.
  - Para comprobarlo hay que **medirlo en un viewport de móvil**, no mirarlo: el script de
    `scratchpad/pw` recorre el DOM buscando elementos cuyo borde derecho supera el ancho de
    la ventana.

## 6.95 Traducción de las pantallas (es / en)

Todo el texto visible pasa por el sistema de localización de ABP: `{{ '::Clave' |
abpLocalization }}` en las plantillas y `LocalizationService.instant('::Clave')` en el
TypeScript. Las claves viven en `Domain.Shared/Localization/IgasConsole/{es,en}.json`.

- **`CoreModule` ya trae el pipe**: no hace falta importar `LocalizationPipe` aparte. Un
  componente que no importe `CoreModule` falla con `NG8004: No pipe found`.
- ⚠️ **Los `.json` van EMBEBIDOS en el ensamblado**: añadir una clave y recargar el
  navegador no basta — hay que **reconstruir y reiniciar la API**, o el front seguirá
  recibiendo el diccionario viejo y la clave se pintará vacía.
- ⚠️ **Un ternario de plantilla no admite el pipe** (`{{ a ? 'Uno' : 'Otro' }}`). Se
  resuelve en el componente con `instant(...)`.
- **Lo que viene del servidor con nombres internos** (el estado de un trabajo:
  `Esperando`, `Reintentando`…) se traduce **en el cliente**, con una clave por valor. No
  se traduce en el servidor a propósito: ese nombre es parte del contrato de la API —el
  filtro se manda con él— y traducirlo allí haría que la respuesta dependiera del idioma
  de quien pregunta.
- **No se traducen** los nombres técnicos que romperían al traducirse: la cabecera
  `X-Api-Key`, el comando `journalctl -u igas-console`, la URL de la API de Telegram.
  Tampoco la página `home`, que es la de bienvenida que trae ABP con enlaces a su propio
  sitio.
- Al comprobarlo en el navegador, la cultura se elige con el **idioma del navegador** y con
  el código exacto configurado (`en`, no `en-US`): una cultura que no encaja no cae al
  idioma por defecto — deja los textos **en blanco**, que parece un fallo de renderizado.
- El barrido de verificación busca `::Algo:Algo` en el texto de la página: una clave sin
  traducir se pinta tal cual, así que es la forma de encontrarlas sin abrir pantalla por
  pantalla.

