using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IgasConsole.Exporting;

/// <summary>Una fila leída, con su número de línea para poder señalarla en los errores.</summary>
public sealed record FilaCsv(int Linea, IReadOnlyList<string> Celdas);

/// <summary>
/// Lee el CSV que genera <see cref="EscritorCsv"/>, y también el que traiga el usuario.
///
/// <para>Es el camino de vuelta del export, y hace falta el <b>primer</b> día de un cliente,
/// no el último: nadie teclea a mano el catálogo que ya tiene en una hoja de cálculo.</para>
///
/// <para><b>Leer es más difícil que escribir, y por eso esto no es un `Split(';')`.</b> Al
/// escribir se controla todo; al leer, el archivo lo produjo Excel, Google Sheets, un export
/// de otro sistema o alguien a mano, y cada uno tiene sus costumbres.</para>
/// </summary>
public static class LectorCsv
{
    /// <summary>
    /// Separadores que se aceptan al leer, en orden de preferencia.
    ///
    /// <para>⚠️ Al escribir se usa <b>siempre</b> punto y coma (ver <c>EscritorCsv</c>), pero
    /// al leer hay que admitir los dos: un Excel en inglés guarda con comas, y ese archivo lo
    /// va a subir alguien igualmente. Rechazarlo por «formato incorrecto» sería técnicamente
    /// defendible y prácticamente inútil.</para>
    ///
    /// <para>Cuál se usa <b>se detecta de la primera línea</b>, no se pregunta: se cuenta cuál
    /// aparece más fuera de comillas. Si empatan o no hay ninguno, manda el punto y coma, que
    /// es el nuestro.</para>
    /// </summary>
    private static readonly char[] SeparadoresPosibles = [';', ','];

    /// <summary>
    /// Parte el contenido en filas y celdas.
    /// </summary>
    /// <param name="contenido">El archivo entero, ya en texto.</param>
    /// <returns>Las filas con contenido, numeradas desde 1 como las ve el usuario.</returns>
    public static IReadOnlyList<FilaCsv> Leer(string contenido)
    {
        if (string.IsNullOrEmpty(contenido))
        {
            return [];
        }

        // ⚠️ Se quita el BOM si viene. `EscritorCsv` lo pone a propósito para que Excel lea
        // los acentos, así que un archivo exportado y vuelto a subir lo trae — y sin quitarlo
        // el primer encabezado sería "﻿Nombre" y no casaría con "Nombre" por ningún
        // lado. Es el fallo perfecto: invisible al mirar el archivo.
        contenido = contenido.TrimStart('﻿');

        var separador = DetectarSeparador(contenido);
        var filas = new List<FilaCsv>();

        var celdas = new List<string>();
        var celda = new StringBuilder();
        var dentroDeComillas = false;
        var linea = 1;
        var hayAlgoEnLaFila = false;

        void CerrarCelda()
        {
            celdas.Add(celda.ToString());
            celda.Clear();
        }

        void CerrarFila()
        {
            CerrarCelda();

            // Las líneas en blanco se saltan: un CSV suele acabar en salto de línea, y una
            // fila vacía al final haría fallar la importación entera por nada.
            if (hayAlgoEnLaFila)
            {
                filas.Add(new FilaCsv(linea, celdas.ToList()));
            }

            celdas.Clear();
            hayAlgoEnLaFila = false;
        }

        for (var i = 0; i < contenido.Length; i++)
        {
            var c = contenido[i];

            if (dentroDeComillas)
            {
                // Dos comillas seguidas dentro de un campo entrecomillado son una comilla
                // literal. Es la convención de RFC 4180 y la que usa Excel.
                if (c == '"' && i + 1 < contenido.Length && contenido[i + 1] == '"')
                {
                    celda.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    dentroDeComillas = false;
                }
                else
                {
                    // ⚠️ Un salto de línea DENTRO de comillas es parte del valor, no una fila
                    // nueva. Pasa de verdad: una descripción con varias líneas escrita en
                    // Excel. Partir por `\n` a secas rompe el archivo justo ahí, y el error
                    // que sale habla de columnas que no cuadran.
                    celda.Append(c);
                    hayAlgoEnLaFila = true;
                }

                continue;
            }

            // ⚠️⚠️ Una comilla solo ABRE campo si está al principio de la celda. En cualquier
            // otra posición es un carácter más.
            //
            // Sin esta condición, un nombre tan normal como `Monitor 27"` —27 pulgadas—
            // abría un campo entrecomillado que no se cerraba nunca, y el lector se tragaba
            // **el resto del archivo entero** dentro de esa celda. El síntoma era
            // desconcertante: se importaban 2 filas de 6 y salía un único error hablando de
            // un precio vacío, sin ninguna pista de que el problema fuera una comilla.
            //
            // Se descubrió probando con un CSV escrito como lo escribiría una persona, no
            // como lo exporta la aplicación. Es la diferencia entre el archivo propio y el
            // que llega de fuera, que es justo para lo que existe una importación.
            if (c == '"' && celda.Length == 0)
            {
                dentroDeComillas = true;
                hayAlgoEnLaFila = true;
            }
            else if (c == separador)
            {
                CerrarCelda();
            }
            else if (c == '\n')
            {
                CerrarFila();
                linea++;
            }
            else if (c != '\r')
            {
                // El \r se ignora: los archivos de Windows traen \r\n y los de Unix \n. Sin
                // esto, la última celda de cada fila arrastraría un \r invisible que hace
                // fallar cualquier comparación.
                celda.Append(c);

                if (!char.IsWhiteSpace(c))
                {
                    hayAlgoEnLaFila = true;
                }
            }
        }

        // Lo que quede sin cerrar es la última fila, cuando el archivo no acaba en salto.
        if (hayAlgoEnLaFila || celdas.Count > 0 || celda.Length > 0)
        {
            CerrarFila();
        }

        return filas;
    }

    /// <summary>
    /// Lee un flujo con un tope de tamaño.
    ///
    /// <para>⚠️ El tope va <b>al leer</b>, no comprobando la longitud declarada. Es la misma
    /// lección que la foto de perfil (§6.1): <c>ContentLength</c> lo pone quien sube y puede
    /// faltar o mentir. Aquí además importa el doble, porque un CSV se procesa entero en
    /// memoria.</para>
    /// </summary>
    public static async System.Threading.Tasks.Task<string> LeerTextoAsync(
        Stream flujo, int maxBytes, string mensajeSiSePasa)
    {
        var bytes = await Files.LectorConTope.LeerAsync(flujo, maxBytes, mensajeSiSePasa);

        // UTF-8 sin lanzar ante bytes inválidos: un archivo guardado en Windows-1252 —que es
        // lo que hace Excel si el usuario elige "CSV" a secas— tiene bytes que no son UTF-8
        // válido. Lanzar convertiría un acento mal codificado en «no se pudo importar nada»;
        // así entra con un carácter de reemplazo y el usuario ve QUÉ fila corregir.
        return new UTF8Encoding(false, throwOnInvalidBytes: false).GetString(bytes);
    }

    private static char DetectarSeparador(string contenido)
    {
        var finDeLinea = contenido.IndexOf('\n');
        var primera = finDeLinea >= 0 ? contenido[..finDeLinea] : contenido;

        var mejor = SeparadoresPosibles[0];
        var mejorCuenta = 0;

        foreach (var candidato in SeparadoresPosibles)
        {
            var cuenta = ContarFueraDeComillas(primera, candidato);

            if (cuenta > mejorCuenta)
            {
                mejor = candidato;
                mejorCuenta = cuenta;
            }
        }

        return mejor;
    }

    private static int ContarFueraDeComillas(string linea, char c)
    {
        var dentro = false;
        var n = 0;

        foreach (var actual in linea)
        {
            if (actual == '"')
            {
                dentro = !dentro;
            }
            else if (actual == c && !dentro)
            {
                n++;
            }
        }

        return n;
    }
}
