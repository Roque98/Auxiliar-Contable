using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;

namespace AuxiliarContable.Files;

/// <summary>
/// Lee un stream entero pero se planta al pasar de un tope de bytes.
///
/// <para>Es la defensa REAL contra una subida enorme. El <c>ContentLength</c> que se mira
/// antes es solo un atajo para cortar sin leer nada: lo declara quien sube y puede faltar o
/// mentir —por ejemplo con <c>Transfer-Encoding: chunked</c>—, así que el límite que cuenta
/// es este, el que se aplica mientras se lee.</para>
///
/// <para>No sirve leer todo y medir después: para entonces el archivo ya está en memoria,
/// que es justo lo que se quiere evitar.</para>
///
/// <para>Vive aquí y no dentro de un application service porque lo usan dos —la foto de
/// perfil y el logo del inquilino—, y un control de seguridad copiado es un control que
/// tarde o temprano se arregla en una copia y no en la otra.</para>
/// </summary>
public static class LectorConTope
{
    public static async Task<byte[]> LeerAsync(
        Stream origen,
        int tope,
        string mensajeSiSePasa,
        CancellationToken cancellationToken = default)
    {
        using var destino = new MemoryStream();
        var trozo = new byte[81920];

        while (true)
        {
            var leidos = await origen.ReadAsync(trozo, cancellationToken);
            if (leidos == 0)
            {
                break;
            }

            if (destino.Length + leidos > tope)
            {
                throw new UserFriendlyException(mensajeSiSePasa);
            }

            await destino.WriteAsync(trozo.AsMemory(0, leidos), cancellationToken);
        }

        return destino.ToArray();
    }
}
