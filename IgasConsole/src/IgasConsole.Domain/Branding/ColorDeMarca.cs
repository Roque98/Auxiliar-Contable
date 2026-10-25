using System;

namespace IgasConsole.Branding;

/// <summary>
/// Valida y normaliza el color de acento.
///
/// <para>⚠️ Esto NO es cosmética: es un control de seguridad. El valor acaba escrito
/// literalmente dentro de un bloque <c>&lt;style&gt;</c> en la página de login del Host y
/// dentro de <c>style.setProperty('--lpx-brand', …)</c> en el navegador. Sin validar,
/// alguien podría guardar <c>red } body { background: url(…) } .x {</c> y estaría
/// <b>inyectando CSS</b> en una página que ven todos los usuarios de su inquilino — CSS que
/// puede tapar el formulario, dibujar uno falso encima o sacar el logo de un tercero.</para>
///
/// <para>Por eso la validación es una <b>lista blanca de forma exacta</b> (<c>#RRGGBB</c>,
/// solo dígitos hexadecimales) y no un intento de limpiar caracteres peligrosos: lo primero
/// solo acepta lo que se sabe inofensivo; lo segundo apuesta a haber pensado en todas las
/// formas de escapar, que es la apuesta que siempre se pierde.</para>
///
/// <para>Se queda deliberadamente fuera lo que un color CSS también admitiría —
/// <c>rgb()</c>, <c>hsl()</c>, nombres, <c>#RGB</c> de tres dígitos, transparencia—: cada
/// forma extra es una gramática más que analizar a cambio de nada, porque el selector de
/// color del navegador emite justo <c>#RRGGBB</c>.</para>
/// </summary>
public static class ColorDeMarca
{
    /// <summary>
    /// Devuelve el color normalizado en minúsculas (<c>#rrggbb</c>), o <c>null</c> si el
    /// texto no es exactamente eso.
    ///
    /// <para>Devolver <c>null</c> en vez de lanzar es a propósito: quien llama decide si un
    /// color inválido es un error del formulario (lo es, al guardar) o un valor viejo que
    /// simplemente se ignora (lo es, al leer — un ajuste corrupto no puede tumbar la
    /// pantalla de nadie).</para>
    /// </summary>
    public static string? Normalizar(string? texto)
    {
        if (texto == null)
        {
            return null;
        }

        var limpio = texto.Trim();

        if (limpio.Length != 7 || limpio[0] != '#')
        {
            return null;
        }

        for (var i = 1; i < limpio.Length; i++)
        {
            if (!Uri.IsHexDigit(limpio[i]))
            {
                return null;
            }
        }

        return limpio.ToLowerInvariant();
    }

    /// <summary>
    /// El color guardado, o el de LeptonX si no hay ninguno o el guardado no es válido.
    /// <para>Es el camino de LECTURA, y por eso nunca falla: si alguien tocó la fila a mano
    /// y dejó basura, se pinta el color de siempre en vez de romper la aplicación.</para>
    /// </summary>
    public static string OPorDefecto(string? guardado)
    {
        return Normalizar(guardado) ?? BrandingConsts.DefaultAccentColor;
    }

    /// <summary>
    /// El mismo color como terna <c>"r, g, b"</c>.
    ///
    /// <para>Hace falta porque Bootstrap guarda el color primario DOS veces: como
    /// <c>--bs-primary</c> y como <c>--bs-primary-rgb</c>, y esta segunda es la que usan sus
    /// variantes con transparencia (<c>rgba(var(--bs-primary-rgb), .25)</c>, el halo de foco
    /// de los campos, por ejemplo). Redefiniendo solo la primera, esos detalles se quedarían
    /// del color anterior y el resultado sería una interfaz de dos colores.</para>
    /// </summary>
    public static string ARgb(string colorNormalizado)
    {
        var c = OPorDefecto(colorNormalizado);

        var r = Convert.ToInt32(c.Substring(1, 2), 16);
        var g = Convert.ToInt32(c.Substring(3, 2), 16);
        var b = Convert.ToInt32(c.Substring(5, 2), 16);

        return $"{r}, {g}, {b}";
    }

    /// <summary>La tinta del design system: el texto oscuro que va sobre un acento claro.</summary>
    public const string TextoOscuro = "#23262b";

    public const string TextoClaro = "#ffffff";

    /// <summary>
    /// El color del texto que va ENCIMA del acento (un botón): el que más contraste dé según
    /// WCAG. Con el amarillo de serie sale la tinta; con un acento oscuro, blanco. Es la misma
    /// regla que usa el front (`contraste.ts`).
    /// </summary>
    public static string TextoSobre(string colorNormalizado)
    {
        var c = OPorDefecto(colorNormalizado);
        var fondo = Luminancia(c);

        var conOscuro = (fondo + 0.05) / (Luminancia(TextoOscuro) + 0.05);
        var conClaro = (Luminancia(TextoClaro) + 0.05) / (fondo + 0.05);

        return conOscuro >= conClaro ? TextoOscuro : TextoClaro;
    }

    private static double Luminancia(string color)
    {
        double Canal(int i)
        {
            var v = Convert.ToInt32(color.Substring(i, 2), 16) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Canal(1) + 0.7152 * Canal(3) + 0.0722 * Canal(5);
    }
}
