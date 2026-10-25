# Tipografía

**Sora**, variable (pesos 400–700), la del prototipo de diseño
(`docs/referencia-ui/panel-administracion.html`), servida **desde aquí** y no desde un
CDN.

- Origen: <https://github.com/sora-xor/sora-font>, vía Google Fonts (v17).
- Licencia: **SIL Open Font License 1.1** — ver `OFL.txt`. Permite el uso, la
  modificación y la redistribución, incluida la comercial; lo único que exige es
  conservar el aviso de licencia, que es lo que hace este directorio.

## Por qué se sirve desde aquí

Pedirla a `fonts.googleapis.com` son dos orígenes más (CSS y `.woff2`) con su DNS y su
TLS en el camino de pintar texto, y un `@import` encadenado que no se paraleliza. Los dos
archivos de aquí pesan **37 KB en total** y salen del mismo dominio, sobre una conexión
ya abierta. `index.html` precarga el de latín.

## Los archivos

Los subconjuntos que sirve Google, tal cual, sin recomprimir:

| Archivo | Para qué |
|---|---|
| `sora-latin.woff2` | Latín básico: todo el español, incluidas tildes y ñ |
| `sora-latin-ext.woff2` | Latín extendido: solo se descarga si una página lo necesita (`unicode-range`) |

Sora no tiene cursiva: si algún texto la pide, el navegador la sintetiza.

La declaración `@font-face` está en `src/styles/igas/_base.scss`.
