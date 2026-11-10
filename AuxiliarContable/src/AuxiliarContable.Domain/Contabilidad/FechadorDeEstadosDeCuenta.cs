using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Pone un estado de cuenta en su mes leyendo su periodo, como hace un sistema contable al importar
/// el estado de cuenta: nadie tiene que acordarse de elegir el mes. Corre en segundo plano después de
/// subirlo (lo lee el motor; un escaneo tarda). Si no se puede leer, el documento queda «con error»
/// y su motivo dice que se le ponga el mes a mano.
/// </summary>
public class FechadorDeEstadosDeCuenta : DomainService
{
    public const string SinPeriodo = "No se pudo leer de qué periodo es: ponle el mes con el calendario.";

    private readonly IRepository<Documento, Guid> _documentos;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IMotorFiscal _motor;
    private readonly GestorDePeriodos _periodos;

    public FechadorDeEstadosDeCuenta(
        IRepository<Documento, Guid> documentos,
        AlmacenDeDocumentos almacen,
        IMotorFiscal motor,
        GestorDePeriodos periodos)
    {
        _documentos = documentos;
        _almacen = almacen;
        _motor = motor;
        _periodos = periodos;
    }

    /// <summary>
    /// El motor caído lanza <see cref="MotorFiscalNoDisponibleException"/> para que el trabajo se
    /// reintente; cualquier otro problema queda anotado en el documento.
    /// </summary>
    public async Task FecharAsync(Guid documentoId)
    {
        var documento = await _documentos.FindAsync(documentoId);
        // Borrado, no es estado de cuenta o alguien ya le puso el mes: lo que dijo una persona manda.
        if (documento == null || documento.Tipo != TipoDeDocumento.EstadoDeCuenta || documento.FechaDelDocumento != null)
        {
            return;
        }

        var lectura = await _motor.LeerEstadosDeCuentaAsync(
            [new ArchivoParaElMotor(documento.NombreOriginal, await _almacen.LeerAsync(documento))]);
        var mes = lectura.Cuentas.Select(MesDe).FirstOrDefault(m => m != null);
        if (mes is not { } delMes)
        {
            documento.MarcarConError(SinPeriodo);
        }
        else if (await _periodos.EstaCerradoAsync(documento.OrganizacionId, delMes.Year, delMes.Month))
        {
            documento.MarcarConError($"Es de {delMes:MM/yyyy}, que ya está cerrado: se queda aquí hasta que el despacho reabra ese periodo y lo cambie de mes.");
        }
        else
        {
            documento.FecharEn(FechaDelCfdi.ComoFecha(delMes));
            documento.MarcarProcesado();
            Logger.LogInformation("Estado de cuenta {Documento} fechado en {Mes:yyyy-MM}", documento.Id, delMes);
        }

        await _documentos.UpdateAsync(documento, autoSave: true);
    }

    /// <summary>
    /// El mes que más días cubre: un periodo del 15 de octubre al 14 de noviembre es de octubre o
    /// noviembre según dónde caiga su mitad. Sin fecha inicial, el del corte.
    /// </summary>
    public static DateOnly? MesDe(CuentaLeida cuenta)
    {
        var final = Leer(cuenta.PeriodoFinal);
        if (final == null)
        {
            return null;
        }

        var inicial = Leer(cuenta.PeriodoInicial) ?? final.Value;
        if (inicial > final)
        {
            inicial = final.Value;
        }

        var mitad = inicial.AddDays((final.Value.DayNumber - inicial.DayNumber) / 2);
        return new DateOnly(mitad.Year, mitad.Month, 1);
    }

    private static DateOnly? Leer(string? texto) =>
        DateOnly.TryParseExact(texto?.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? dia
            : null;
}
