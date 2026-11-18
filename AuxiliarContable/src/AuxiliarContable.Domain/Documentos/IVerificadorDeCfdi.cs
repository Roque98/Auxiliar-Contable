using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.Documentos;

/// <summary>
/// El servicio público de verificación de CFDI del SAT (el mismo del código QR de la factura): con RFC del emisor, del
/// receptor, total y UUID dice si está vigente, cancelada o con una cancelación en proceso. No pide contraseña ni captcha.
/// La implementación real vive en el host (<c>VerificadorDeCfdiSat</c>).
/// </summary>
public interface IVerificadorDeCfdi
{
    bool EstaConfigurado { get; }

    /// <summary>Lanza <see cref="VerificadorNoDisponibleException"/> si el SAT no responde.</summary>
    Task<EstatusDelSat> ConsultarAsync(string rfcEmisor, string rfcReceptor, string total, string uuid, CancellationToken cancellationToken = default);
}

/// <summary>Lo que contesta el SAT, tal cual.</summary>
public sealed record EstatusDelSat(string Estado, string? EstatusCancelacion, string? EsCancelable, string? CodigoEstatus)
{
    public const string Vigente = "Vigente";
    public const string Cancelado = "Cancelado";
    public const string EnProcesoDeCancelacion = "En proceso de cancelación";
    public const string NoEncontrado = "No encontrado en el SAT";

    /// <summary>Lo que se guarda en <see cref="Documento.EstatusSat"/>: Vigente, Cancelado, En proceso de cancelación o No encontrado.</summary>
    public string ParaGuardar
    {
        get
        {
            var estado = Estado.Trim();
            if (estado.Equals(Cancelado, StringComparison.OrdinalIgnoreCase))
            {
                return Cancelado;
            }

            if (estado.Equals("No Encontrado", StringComparison.OrdinalIgnoreCase))
            {
                return NoEncontrado;
            }

            var proceso = (EstatusCancelacion ?? "").ToLowerInvariant();
            if ((proceso.Contains("proceso") || proceso.Contains("solicitud")) && !proceso.Contains("rechaz"))
            {
                return EnProcesoDeCancelacion;
            }

            return estado.Equals(Vigente, StringComparison.OrdinalIgnoreCase) ? Vigente : estado;
        }
    }
}

public class VerificadorNoDisponibleException : AbpException
{
    public VerificadorNoDisponibleException(string mensaje, Exception? interna = null) : base(mensaje, interna)
    {
    }
}

/// <summary>Sin host (DbMigrator, tests) no se llama al SAT.</summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IVerificadorDeCfdi))]
public class VerificadorDeCfdiSinConfigurar : IVerificadorDeCfdi, ISingletonDependency
{
    public bool EstaConfigurado => false;

    public Task<EstatusDelSat> ConsultarAsync(string rfcEmisor, string rfcReceptor, string total, string uuid, CancellationToken cancellationToken = default) =>
        throw new VerificadorNoDisponibleException("No hay verificador del SAT en este proceso.");
}
