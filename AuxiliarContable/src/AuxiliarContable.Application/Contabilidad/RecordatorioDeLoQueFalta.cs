using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Notifications;
using AuxiliarContable.Organizaciones;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Linq;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Lo que el cliente dijo que subiría después (pata Pendiente con motivo, <see cref="Operacion.Posponer"/>) y no ha subido:
/// a los <see cref="DiasDeGracia"/> días se le recuerda a cada persona de la organización que sube información, con un aviso
/// en la campana y un correo (que manda el trabajo del host). Como mucho uno cada <see cref="DiasEntreRecordatorios"/> días
/// por persona y cliente: el freno es el propio aviso.
/// </summary>
public class RecordatorioDeLoQueFalta : ITransientDependency
{
    public const string Titulo = "Tienes documentos que dijiste que subirías después";
    public const int DiasDeGracia = 3;
    public const int DiasEntreRecordatorios = 7;
    public const int MaximoEnElCorreo = 15;

    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly INotificacionRepository _avisos;
    private readonly IIdentityUserRepository _usuarios;
    private readonly IGuidGenerator _guids;
    private readonly IAsyncQueryableExecuter _consultas;

    public RecordatorioDeLoQueFalta(
        IRepository<Operacion, Guid> operaciones,
        IRepository<Organizacion, Guid> organizaciones,
        INotificacionRepository avisos,
        IIdentityUserRepository usuarios,
        IGuidGenerator guids,
        IAsyncQueryableExecuter consultas)
    {
        _operaciones = operaciones;
        _organizaciones = organizaciones;
        _avisos = avisos;
        _usuarios = usuarios;
        _guids = guids;
        _consultas = consultas;
    }

    /// <summary>Título del aviso para un cliente: el freno distingue clientes de la misma persona.</summary>
    public static string TituloPara(string organizacion) => $"{Titulo} ({organizacion})";

    /// <summary>Deja los avisos en la campana y devuelve los correos por mandar.</summary>
    public async Task<List<RecordatorioParaEnviar>> PrepararAsync(DateTime ahoraUtc)
    {
        var limite = ahoraUtc.AddDays(-DiasDeGracia);
        var pendientes = await _consultas.ToListAsync((await _operaciones.GetQueryableAsync())
            .Where(o => (o.LastModificationTime ?? o.CreationTime) <= limite
                        && ((o.FacturaEstado == EstadoDePata.Pendiente && o.FacturaMotivo != null)
                            || (o.TransaccionEstado == EstadoDePata.Pendiente && o.TransaccionMotivo != null)
                            || (o.ControlEstado == EstadoDePata.Pendiente && o.ControlMotivo != null))));
        var porCliente = pendientes
            .SelectMany(o => Patas(o, ahoraUtc))
            .GroupBy(x => x.OrganizacionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Dias).ToList());
        if (porCliente.Count == 0)
        {
            return [];
        }

        var ids = porCliente.Keys.ToList();
        var organizaciones = await _consultas.ToListAsync(
            (await _organizaciones.WithDetailsAsync(o => o.Miembros)).Where(o => ids.Contains(o.Id)));
        var desde = ahoraUtc.AddDays(-DiasEntreRecordatorios);
        var r = new List<RecordatorioParaEnviar>();
        foreach (var org in organizaciones)
        {
            var cosas = porCliente[org.Id];
            var titulo = TituloPara(org.Nombre);
            foreach (var miembro in org.Miembros.Where(m => m.Rol >= RolEnOrganizacion.Miembro))
            {
                if (await _avisos.ExistsRecentAsync(titulo, miembro.UserId, desde))
                {
                    continue;
                }

                var usuario = await _usuarios.FindAsync(miembro.UserId);
                if (usuario == null || !usuario.IsActive)
                {
                    continue;
                }

                var url = $"/mi-informacion/todo?cliente={org.Id}";
                await _avisos.InsertAsync(new Notificacion(
                    _guids.Create(), usuario.TenantId, usuario.Id, requiredPermission: null, NotificationSeverity.Info, titulo,
                    $"{cosas.Count} documento(s) pendiente(s): " + string.Join("; ", cosas.Take(3).Select(c => c.Texto))
                    + (cosas.Count > 3 ? "…" : ".") + " Mientras falten, la contabilidad y el cálculo de impuestos quedan incompletos.",
                    url, ahoraUtc));
                if (!string.IsNullOrWhiteSpace(usuario.Email))
                {
                    r.Add(new RecordatorioParaEnviar
                    {
                        Email = usuario.Email,
                        Nombre = string.IsNullOrWhiteSpace(usuario.Name) ? usuario.UserName : usuario.Name,
                        Organizacion = org.Nombre,
                        Url = url,
                        Pendientes = cosas.Take(MaximoEnElCorreo).ToList(),
                        Total = cosas.Count,
                    });
                }
            }
        }

        return r;
    }

    private static IEnumerable<PendienteDeSubir> Patas(Operacion o, DateTime ahoraUtc)
    {
        var dias = Math.Max(0, (int)(ahoraUtc - (o.LastModificationTime ?? o.CreationTime)).TotalDays);
        var es = CultureInfo.GetCultureInfo("es-MX");
        foreach (var (pata, estado, motivo) in new[]
                 {
                     ("la factura", o.FacturaEstado, o.FacturaMotivo),
                     ("el comprobante de la transacción", o.TransaccionEstado, o.TransaccionMotivo),
                     ("el ticket o comprobante interno", o.ControlEstado, o.ControlMotivo),
                 })
        {
            if (estado == EstadoDePata.Pendiente && !string.IsNullOrWhiteSpace(motivo)
                && !motivo.StartsWith(OperacionesAppService.MotivoDelEstadoDeCuenta, StringComparison.Ordinal))
            {
                yield return new PendienteDeSubir
                {
                    OrganizacionId = o.OrganizacionId,
                    Que = pata,
                    Operacion = o.Descripcion,
                    Fecha = o.Fecha.ToString("d 'de' MMMM", es),
                    Importe = o.Importe?.ToString("C2", es),
                    Dias = dias,
                };
            }
        }
    }
}

public class RecordatorioParaEnviar
{
    public string Email { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Organizacion { get; set; } = "";

    /// <summary>Ruta de la consola (sin dominio) donde se sube lo que falta.</summary>
    public string Url { get; set; } = "";

    public List<PendienteDeSubir> Pendientes { get; set; } = [];
    public int Total { get; set; }
}

public class PendienteDeSubir
{
    public Guid OrganizacionId { get; set; }

    /// <summary>«la factura», «el comprobante de la transacción»…</summary>
    public string Que { get; set; } = "";

    public string Operacion { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string? Importe { get; set; }
    public int Dias { get; set; }

    public string Texto => $"{Que} de «{Operacion}» ({Fecha}{(Importe != null ? ", " + Importe : "")})";
}
