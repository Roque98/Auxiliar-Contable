/*
 * Scripts de las páginas del servidor (login, dos pasos, recuperar contraseña).
 *
 * Un formulario se envía UNA vez. Pasó en producción: dos toques seguidos en «Entrar» mandaron
 * dos POST; el primero inició sesión y el segundo llegó con el token antifalsificación de
 * cuando aún no había sesión → AntiforgeryValidationException → página de error 400, y desde
 * ahí la persona acabó fuera de la consola.
 *
 * ⚠️ NO se deshabilita el botón: el de «Entrar» lleva name/value propios (Action=Login) y un
 * botón deshabilitado puede dejar de viajar en el envío. Se marca el formulario y se ignoran
 * los envíos siguientes; el botón solo se ve ocupado.
 */
(function () {
    'use strict';

    document.addEventListener('submit', function (evento) {
        var formulario = evento.target;
        // Si la validación del formulario canceló el envío, no cuenta: se puede corregir y reenviar.
        if (!(formulario instanceof HTMLFormElement) || evento.defaultPrevented) {
            return;
        }
        if (formulario.dataset.igEnviando === '1') {
            evento.preventDefault();
            return;
        }
        formulario.dataset.igEnviando = '1';
        formulario.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(function (boton) {
            boton.setAttribute('aria-busy', 'true');
            boton.classList.add('ig-enviando');
        });
    });

    // Al volver con «atrás» (la página sale de la caché del navegador), el formulario se reactiva.
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('form[data-ig-enviando]').forEach(function (formulario) {
            delete formulario.dataset.igEnviando;
            formulario.querySelectorAll('.ig-enviando').forEach(function (boton) {
                boton.removeAttribute('aria-busy');
                boton.classList.remove('ig-enviando');
            });
        });
    });
})();
