using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Documentos;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Contabilidad;

/// <summary>Ver <see cref="IControlInternoAppService"/>.</summary>
[Authorize]
public class ControlInternoAppService : AuxiliarContableAppService, IControlInternoAppService
{
    private readonly ArmadorDeExpedientes _armador;
    private readonly IRepository<EvidenciaDeOperacion, Guid> _evidencias;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly GestorDePeriodos _periodos;
    private readonly Autorizador _autorizador;

    public ControlInternoAppService(
        ArmadorDeExpedientes armador,
        IRepository<EvidenciaDeOperacion, Guid> evidencias,
        IRepository<Documento, Guid> documentos,
        GestorDePeriodos periodos,
        Autorizador autorizador)
    {
        _armador = armador;
        _evidencias = evidencias;
        _documentos = documentos;
        _periodos = periodos;
        _autorizador = autorizador;
    }

    public async Task<ExpedienteDelMesDto> GetAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        ValidarMes(anio, mes);
        var (operaciones, hayConciliacion) = await _armador.ArmarAsync(organizacionId, anio, mes);
        var cerrado = await _periodos.EstaCerradoAsync(organizacionId, anio, mes);
        return new ExpedienteDelMesDto
        {
            Anio = anio,
            Mes = mes,
            Operaciones = operaciones,
            HayConciliacion = hayConciliacion,
            Completas = operaciones.Count(o => o.Estado == EstadoDeEvidencia.Completa),
            FaltaMaterialidad = operaciones.Count(o => o.Estado == EstadoDeEvidencia.FaltaMaterialidad),
            FaltaComprobante = operaciones.Count(o => o.Estado == EstadoDeEvidencia.FaltaComprobante),
            PuedeAgregar = !cerrado && (await _autorizador.QuienMiraAsync()).Puede(organizacionId, AccionEnOrganizacion.Subir),
            Cerrado = cerrado,
        };
    }

    public async Task<EvidenciaDto> AgregarAsync(Guid organizacionId, AgregarEvidenciaDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        ValidarMes(input.Anio, input.Mes);
        if (!Enum.IsDefined(input.Tipo) || !Enum.IsDefined(input.Origen))
        {
            throw new UserFriendlyException("Tipo de evidencia no válido.");
        }

        await _periodos.ExigirAbiertoAsync(organizacionId, new DateTime(input.Anio, input.Mes, 1));

        // La operación tiene que ser de este cliente y de ese mes: no se cuelga evidencia de algo que no existe.
        var (operaciones, _) = await _armador.ArmarAsync(organizacionId, input.Anio, input.Mes);
        var existe = input.Origen == OrigenDeOperacion.Banco
            ? input.Clave is { } c && operaciones.Any(o => o.Origen == OrigenDeOperacion.Banco && o.Clave == c)
            : input.MovimientoFueraDelBancoId is { } id && operaciones.Any(o => o.MovimientoFueraDelBancoId == id);
        if (!existe)
        {
            throw new UserFriendlyException("Esa operación no está en ese mes del cliente (¿se volvió a conciliar?).");
        }

        Documento? documento = null;
        if (input.DocumentoId is { } docId)
        {
            documento = await _documentos.FirstOrDefaultAsync(d => d.Id == docId && d.OrganizacionId == organizacionId)
                        ?? throw new EntityNotFoundException(typeof(Documento), docId);
        }

        var e = new EvidenciaDeOperacion(GuidGenerator.Create(), organizacionId, input.Anio, input.Mes, input.Origen,
            input.Origen == OrigenDeOperacion.Banco ? input.Clave : null,
            input.Origen == OrigenDeOperacion.FueraDelBanco ? input.MovimientoFueraDelBancoId : null,
            input.Tipo, input.DocumentoId, input.Nota);
        await _evidencias.InsertAsync(e, autoSave: true);
        return new EvidenciaDto
        {
            Id = e.Id, Tipo = e.Tipo, DocumentoId = e.DocumentoId, NombreDelDocumento = documento?.NombreOriginal, Nota = e.Nota,
            Cuando = e.CreationTime,
        };
    }

    public async Task QuitarAsync(Guid organizacionId, Guid evidenciaId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var e = await _evidencias.FirstOrDefaultAsync(x => x.Id == evidenciaId && x.OrganizacionId == organizacionId)
                ?? throw new EntityNotFoundException(typeof(EvidenciaDeOperacion), evidenciaId);
        await _periodos.ExigirAbiertoAsync(organizacionId, new DateTime(e.Anio, e.Mes, 1));
        await _evidencias.DeleteAsync(e, autoSave: true);
    }

    private static void ValidarMes(int anio, int mes)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }
    }
}
