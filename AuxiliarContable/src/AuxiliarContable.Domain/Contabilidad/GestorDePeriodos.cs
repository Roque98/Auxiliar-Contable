using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>Las reglas del periodo que comparten la bóveda, la conciliación y el fechado automático.</summary>
public class GestorDePeriodos : DomainService
{
    private readonly IRepository<PeriodoContable, Guid> _periodos;

    public GestorDePeriodos(IRepository<PeriodoContable, Guid> periodos)
    {
        _periodos = periodos;
    }

    public async Task<bool> EstaCerradoAsync(Guid organizacionId, int anio, int mes) =>
        await _periodos.AnyAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes == mes && p.Cerrado);

    /// <summary>Lanza si el mes de esa fecha ya se cerró: lo que se presentó no cambia sin reabrirlo.</summary>
    public async Task ExigirAbiertoAsync(Guid organizacionId, DateTime fecha)
    {
        if (await EstaCerradoAsync(organizacionId, fecha.Year, fecha.Month))
        {
            throw new UserFriendlyException(
                $"El periodo {fecha:MM/yyyy} ya está cerrado: para cambiar sus documentos, alguien del despacho tiene que reabrirlo en «Conciliación».");
        }
    }

    /// <summary>
    /// De qué archivos salió una conciliación: si se sube, borra o mueve un documento del mes, la
    /// huella cambia y la conciliación guardada ya no vale.
    /// </summary>
    public static string HuellaDe(IEnumerable<Documento> documentos) => HuellaDe(documentos.Select(d => (d.Id, d.Sha256)));

    public static string HuellaDe(IEnumerable<(Guid Id, string Sha256)> documentos)
    {
        var texto = string.Join('\n', documentos.OrderBy(d => d.Id).Select(d => $"{d.Id:N}:{d.Sha256}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
    }
}
