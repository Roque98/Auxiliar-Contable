using System;

namespace IgasConsole;

/// <summary>
/// Cuánto log se guarda y durante cuánto.
///
/// <para>⚠️ Vive en <c>Logging/</c> y <b>no</b> en <c>Logs/</c>, que es donde se puso primero.
/// El <c>.gitignore</c> tiene <c>Logs/</c> a secas —para no versionar los archivos que
/// escribe la aplicación—, así que este archivo <b>quedó fuera del commit sin un solo
/// aviso</b>: en local compilaba porque estaba en el disco, y el CI reventó con seis
/// <c>CS0103: The name 'LogConsts' does not exist</c>. La puerta de calidad hizo su trabajo y
/// producción no llegó a tocarse, pero conviene no repetirlo: <b>cuidado al crear carpetas
/// con nombres que el .gitignore usa para otra cosa</b>.</para>
///
/// <para>Existe porque no se guardaba <b>nada</b> de esto: el archivo era
/// <c>Logs/logs.txt</c> a secas, <b>un solo archivo que no rotaba nunca</b>. Medido en
/// producción a los cuatro días de vida: <b>40 MB</b>, unos 10 MB al día y subiendo. No
/// llenaba el disco a corto plazo (96 GB, 6 % usado), pero crecía sin techo y sin ninguna
/// forma de decir «esto es de hace tres semanas».</para>
///
/// <para>⚠️ Los valores están escritos <b>dos veces</b>: aquí, para el logger de arranque, y
/// en la sección <c>Serilog</c> de <c>appsettings.json</c>, para el de verdad. No hay forma de
/// evitarlo — el de arranque se crea <b>antes</b> de que exista configuración que leer, que es
/// justamente para qué sirve. Si se cambian, hay que cambiarlos en los dos sitios; si se
/// separan, el archivo del arranque deja de rotar y nadie se entera.</para>
///
/// <para><b>Por qué 30 días.</b> Es lo que hace falta para responder «¿qué pasó hace tres
/// semanas?», que es la pregunta concreta que hoy no se puede contestar. Más tiempo tiene poco
/// valor aquí: lo que interesa a largo plazo —peticiones, cambios de entidades, inicios de
/// sesión, fallos de trabajos— ya vive en la <b>base de datos</b> con su propia retención
/// (§6.15.1, §6.15.2, §6.0.1), entra en las copias y se consulta desde las pantallas. Este
/// archivo es para lo otro: la traza suelta y la excepción con su pila.</para>
/// </summary>
public static class LogConsts
{
    /// <summary>
    /// 30 días. El tope real es este y no el número de archivos: con el corte por tamaño, un
    /// día movido puede generar varios, así que contar archivos no equivale a contar días.
    /// </summary>
    public static readonly TimeSpan Retencion = TimeSpan.FromDays(30);

    /// <summary>
    /// 50 MB por archivo. Al llegar, se corta y sigue en otro (<c>_001</c>, <c>_002</c>…) en
    /// vez de dejar de escribir, que es lo que hace Serilog por defecto al tocar el límite.
    /// Un archivo de este tamaño todavía se abre con un editor normal.
    /// </summary>
    public const long TamanoMaximoPorArchivo = 50L * 1024 * 1024;

    /// <summary>
    /// Cortafuegos por si el volumen se dispara: 120 archivos son 30 días con margen de sobra
    /// para varios cortes por tamaño al día. Si una tormenta de errores llegara a rebasarlo,
    /// se pierde historial antiguo pero <b>no se llena el disco</b> — que es el fallo que
    /// tumbaría la aplicación entera, y con ella la única forma de saber por qué.
    /// </summary>
    public const int MaximoDeArchivos = 120;
}
