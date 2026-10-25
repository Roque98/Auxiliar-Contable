# Marca de Auxiliar contable

`isotipo.svg` es el original: una hoja de registro con la palomita de «conciliado». Azul marino
`#2c5282` (el acento, `--ig-acento`) y amarillo `#f2b632` (solo en el isotipo: con texto blanco
encima no llega a AA, así que no es acento). El nombre va en texto, no en un logotipo dibujado:
el Owner lo cambia en «Marca».

## Sacar los PNG

Con el Chromium que instala Playwright (`npx playwright install chromium`), para cada lado `N`:

```bash
printf '<body style="margin:0"><img src="isotipo.svg" style="display:block;width:%spx;height:%spx">' N N > r.html
chrome --headless --hide-scrollbars --default-background-color=00000000 \
  --window-size=N,N --screenshot=isotipo-N.png file:///ruta/a/docs/marca/r.html
```

Dónde va cada uno:

| Archivo | Tamaño | Uso |
|---|---|---|
| `angular/src/assets/images/logo/isotipo.png` | 256 | menú, acceso, carga |
| `angular/src/assets/images/logo/isotipo-{32,180,192,512}.png` | su nombre | favicon, iOS, PWA |
| `src/*.HttpApi.Host/wwwroot/images/logo/marca/isotipo{,-32,-180}.png` | 256, 32, 180 | login del servidor y correos |
| `favicon.ico` (front y host) | 16 + 32 + 48 | pestaña del navegador |
