using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace IgasConsole.Exporting;

/// <summary>
/// Genera CSV pensado para abrirse en Excel sin sustos.
///
/// <para>Se escribe a mano en vez de traer una librería porque lo que aquí importa no es
/// generar comas —eso son diez líneas— sino las tres decisiones de abajo, que casi ninguna
/// librería toma por ti.</para>
/// </summary>
public static class EscritorCsv
{
    /// <summary>
    /// Separador. Punto y coma y NO coma.
    ///
    /// <para>Excel no lee el separador del archivo: usa el de la configuración regional. En
    /// español (y en buena parte de Europa) ese separador es el punto y coma, así que un CSV
    /// con comas se abre con TODA la fila metida en la primera columna. Es el fallo más
    /// típico de un export "que funcionaba en mi máquina".</para>
    ///
    /// <para>El precio: herramientas que asumen coma (pandas por defecto, por ejemplo)
    /// necesitan que se les diga <c>sep=';'</c>. Si este template se usa con Excel en
    /// inglés, cambia esta constante a ',' y ya está.</para>
    /// </summary>
    private const char Separador = ';';

    /// <summary>
    /// Caracteres que convierten una celda en fórmula al abrirla en Excel o Sheets.
    /// </summary>
    private static readonly char[] IniciosPeligrosos = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// Construye el CSV completo, con BOM incluido.
    /// </summary>
    /// <param name="cabeceras">Nombres de columna, en orden.</param>
    /// <param name="filas">Cada fila, ya convertida a texto, en el mismo orden.</param>
    public static byte[] Generar(IEnumerable<string> cabeceras, IEnumerable<IEnumerable<string?>> filas)
    {
        var sb = new StringBuilder();

        sb.AppendLine(string.Join(Separador, cabeceras.Select(Escapar)));

        foreach (var fila in filas)
        {
            sb.AppendLine(string.Join(Separador, fila.Select(Escapar)));
        }

        // ⚠️ CON BOM. Excel abre los .csv en la codificación regional (Windows-1252 aquí), no
        // en UTF-8, salvo que el archivo empiece por el BOM. Sin él, "Ratón" se ve "RatÃ³n" y
        // parece un problema de la base de datos cuando es del lector.
        // encoderShouldEmitUTF8Identifier: true es exactamente eso.
        var utf8ConBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preambulo = utf8ConBom.GetPreamble();
        var contenido = utf8ConBom.GetBytes(sb.ToString());

        // GetBytes NO añade el preámbulo por su cuenta (solo lo hace un StreamWriter): hay
        // que anteponerlo a mano.
        var resultado = new byte[preambulo.Length + contenido.Length];
        preambulo.CopyTo(resultado, 0);
        contenido.CopyTo(resultado, preambulo.Length);
        return resultado;
    }

    /// <summary>Formatea una fecha UTC de forma que Excel y una persona la entiendan igual.</summary>
    public static string Fecha(DateTime utc)
    {
        // Formato invariable y explícito sobre que es UTC: dejarlo a la cultura del servidor
        // hace que el mismo export salga distinto según dónde corra.
        return utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
    }

    public static string Numero(int valor) => valor.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapa una celda: comillas dobles cuando hace falta y, sobre todo, neutraliza las
    /// fórmulas.
    /// </summary>
    private static string Escapar(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return string.Empty;
        }

        var texto = valor;

        // ⚠️ INYECCIÓN DE FÓRMULAS (CSV injection). Excel y Google Sheets EJECUTAN cualquier
        // celda que empiece por = + - @. Estos exports llevan datos que escribe cualquiera
        // desde fuera —URLs, nombres de usuario, cabeceras—, así que una celda podría llegar
        // como =HYPERLINK(...) o algo peor y ejecutarse en cuanto alguien abra el archivo.
        // Anteponer un apóstrofo fuerza a Excel a tratarla como texto.
        // No basta con escapar comillas: eso arregla el formato, no esto.
        if (IniciosPeligrosos.Contains(texto[0]))
        {
            texto = "'" + texto;
        }

        // Reglas normales de CSV: si hay separador, comillas o saltos, se entrecomilla todo
        // y las comillas internas se duplican.
        if (texto.Contains(Separador) || texto.Contains('"') || texto.Contains('\n') || texto.Contains('\r'))
        {
            texto = "\"" + texto.Replace("\"", "\"\"") + "\"";
        }

        return texto;
    }

    /// <summary>Nombre de archivo con marca de tiempo, para que dos descargas no se pisen.</summary>
    public static string NombreConFecha(string prefijo)
    {
        return $"{prefijo}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
    }
}
