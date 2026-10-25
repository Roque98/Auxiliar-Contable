using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuxiliarContable.Antivirus;

/// <summary>Sección <c>Antivirus</c>. Sin <see cref="Host"/> no se escanea (desarrollo, tests).</summary>
public class AntivirusOptions
{
    public const string Seccion = "Antivirus";

    /// <summary>Dónde escucha clamd (en el VPS, <c>127.0.0.1</c>; ver deploy/README.md).</summary>
    public string? Host { get; set; }

    public int Puerto { get; set; } = 3310;

    public int TimeoutSegundos { get; set; } = 30;
}

/// <summary>
/// Escanea con clamd por su protocolo INSTREAM: <c>zINSTREAM\0</c>, trozos con su tamaño en 4
/// bytes big-endian, y un trozo de tamaño 0 para terminar. Respuesta: <c>stream: OK</c> o
/// <c>stream: &lt;firma&gt; FOUND</c>. Nada se escribe a disco para escanear.
/// </summary>
public class ClamAvEscaner : IEscanerDeArchivos
{
    private const int Trozo = 64 * 1024;

    private readonly AntivirusOptions _opciones;
    private readonly ILogger<ClamAvEscaner> _logger;

    public ClamAvEscaner(IOptions<AntivirusOptions> opciones, ILogger<ClamAvEscaner> logger)
    {
        _opciones = opciones.Value;
        _logger = logger;
    }

    public async Task<ResultadoDelEscaneo> EscanearAsync(byte[] contenido, CancellationToken cancellationToken = default)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(TimeSpan.FromSeconds(_opciones.TimeoutSegundos));
        try
        {
            using var cliente = new TcpClient();
            await cliente.ConnectAsync(_opciones.Host!, _opciones.Puerto, limite.Token);
            await using var flujo = cliente.GetStream();

            await flujo.WriteAsync("zINSTREAM\0"u8.ToArray(), limite.Token);
            var tamano = new byte[4];
            for (var i = 0; i < contenido.Length; i += Trozo)
            {
                var n = Math.Min(Trozo, contenido.Length - i);
                BinaryPrimitives.WriteInt32BigEndian(tamano, n);
                await flujo.WriteAsync(tamano, limite.Token);
                await flujo.WriteAsync(contenido.AsMemory(i, n), limite.Token);
            }

            BinaryPrimitives.WriteInt32BigEndian(tamano, 0);
            await flujo.WriteAsync(tamano, limite.Token);

            using var respuesta = new MemoryStream();
            await flujo.CopyToAsync(respuesta, limite.Token);
            return Interpretar(Encoding.ASCII.GetString(respuesta.ToArray()));
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("ClamAV no respondió en {Host}:{Puerto}: {Error}", _opciones.Host, _opciones.Puerto, ex.Message);
            return new ResultadoDelEscaneo(VeredictoDelEscaneo.NoDisponible, "ClamAV no respondió");
        }
    }

    public static ResultadoDelEscaneo Interpretar(string respuesta)
    {
        var texto = respuesta.TrimEnd('\0', '\n', '\r', ' ');
        if (texto.EndsWith("OK", StringComparison.Ordinal))
        {
            return ResultadoDelEscaneo.Limpio;
        }

        if (texto.EndsWith("FOUND", StringComparison.Ordinal))
        {
            var firma = texto.Replace("stream:", string.Empty).Replace("FOUND", string.Empty).Trim();
            return new ResultadoDelEscaneo(VeredictoDelEscaneo.Infectado, firma);
        }

        // «INSTREAM size limit exceeded» y otros errores de clamd.
        return new ResultadoDelEscaneo(VeredictoDelEscaneo.NoDisponible, texto.Length <= 200 ? texto : texto[..200]);
    }
}
