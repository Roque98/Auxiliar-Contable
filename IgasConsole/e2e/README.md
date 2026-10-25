# Pruebas de pantalla (Playwright)

Recorren la consola en un navegador de verdad y comprueban lo que no ven las pruebas del
backend ni las unitarias del front:

- ninguna pantalla se desborda a lo ancho (390 px y 1280 px);
- en modo oscuro no quedan fondos blancos ni textos con poco contraste (< 3:1);
- el cambio de tema es un fundido;
- Usuarios: se elige un solo rol y el Developer no se ofrece;
- Seguridad y Ayuda muestran lo que tienen que mostrar;
- un enlace de registro inválido se explica.

## Cómo correrlas

Necesitan la consola LOCAL arrancada (no la levantan solas):

```bash
# 1. API (desde IgasConsole/)
dotnet run --project src/IgasConsole.HttpApi.Host
# 2. Angular (desde IgasConsole/angular)
yarn start
# 3. Pruebas (desde IgasConsole/e2e), la primera vez con npm install
npm install
npm run instalar-navegador   # solo la primera vez
npm test
```

Variables opcionales: `IGAS_E2E_URL` (por defecto `http://localhost:4201`),
`IGAS_E2E_USUARIO` y `IGAS_E2E_CLAVE` (por defecto el admin de desarrollo de ABP).

Si algo falla, `npm run informe` abre el informe con la captura y la traza del fallo.

## Por qué van en serie y con una sola sesión

El login de la consola rota el refresh token. Dos navegadores que arrancan con la misma
sesión guardada reusan un token ya canjeado, y OpenIddict lo toma por robo y revoca la
sesión entera. Además, las rutas de login tienen límite de peticiones por IP. Por eso hay un
solo worker y todas las pruebas comparten el inicio de sesión del `beforeAll`.
