using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using AuxiliarContable.Invitaciones;
using AuxiliarContable.Monitoring;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.Permissions;
using AuxiliarContable.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable.Actividad;

/// <summary>
/// Ver <see cref="IActividadAppService"/>. Junta tres fuentes y las cuenta en frases:
/// <list type="bullet">
/// <item>el registro de llamadas de ABP (a quién invitó, qué organización editó…);</item>
/// <item>el registro de seguridad (entradas, contraseñas, dos pasos);</item>
/// <item>la tabla de invitaciones (quién creó su cuenta y con la invitación de quién).</item>
/// </list>
///
/// <para>⚠️ Jerarquía: solo aparece lo que hicieron personas que quien mira alcanza (y él
/// mismo). El Developer no sale nunca para nadie más.</para>
///
/// <para><c>[DisableAuditing]</c>: mirar la actividad no es actividad; si no, cada vistazo
/// ensuciaría el propio registro.</para>
/// </summary>
[Authorize(AuxiliarContablePermissions.Actividad.Default)]
[DisableAuditing]
public class ActividadAppService : AuxiliarContableAppService, IActividadAppService
{
    private const int TamanoDePagina = 40;

    /// <summary>Repeticiones seguidas de lo mismo dentro de esta ventana se cuentan como una.</summary>
    private static readonly TimeSpan VentanaDeRepeticion = TimeSpan.FromMinutes(30);

    private static readonly string SvcOrganizaciones = typeof(OrganizacionAppService).FullName!;
    private static readonly string SvcInvitaciones = typeof(InvitacionAppService).FullName!;
    private static readonly string SvcUsuarios = typeof(AuxiliarIdentityUserAppService).FullName!;
    private static readonly string SvcSeguridad = typeof(SeguridadDeUsuariosAppService).FullName!;

    private static readonly string[] Servicios = [SvcOrganizaciones, SvcInvitaciones, SvcUsuarios, SvcSeguridad];

    private static readonly string[] Metodos =
    [
        "CreateAsync", "UpdateAsync", "DeleteAsync", "AgregarMiembroAsync", "QuitarMiembroAsync",
        "ReenviarAsync", "RevocarAsync", "QuitarSegundoFactorAsync",
    ];

    private static readonly string[] AccionesDeSesion =
    [
        IdentitySecurityLogActionConsts.LoginSucceeded,
        IdentitySecurityLogActionConsts.LoginFailed,
        IdentitySecurityLogActionConsts.LoginInvalidUserNameOrPassword,
        IdentitySecurityLogActionConsts.LoginLockedout,
        IdentitySecurityLogActionConsts.ChangePassword,
        IdentitySecurityLogActionConsts.TwoFactorEnabled,
        IdentitySecurityLogActionConsts.TwoFactorDisabled,
    ];

    private readonly IRegistroDeActividad _registro;
    private readonly IRepository<Invitacion, Guid> _invitaciones;
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly IRepository<IdentityUser, Guid> _usuarios;
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly IDataFilter _filtros;

    public ActividadAppService(
        IRegistroDeActividad registro,
        IRepository<Invitacion, Guid> invitaciones,
        IRepository<Organizacion, Guid> organizaciones,
        IRepository<IdentityUser, Guid> usuarios,
        JerarquiaDeUsuarios jerarquia,
        IDataFilter filtros)
    {
        _registro = registro;
        _invitaciones = invitaciones;
        _organizaciones = organizaciones;
        _usuarios = usuarios;
        _jerarquia = jerarquia;
        _filtros = filtros;
    }

    public async Task<OpcionesDeActividadDto> GetOpcionesAsync()
    {
        var ocultos = (await _jerarquia.UsuariosFueraDeAlcanceAsync()).ToHashSet();
        var personas = await AsyncExecuter.ToListAsync(
            (await _usuarios.GetQueryableAsync()).Where(u => !ocultos.Contains(u.Id)).OrderBy(u => u.UserName));
        var organizaciones = await AsyncExecuter.ToListAsync(
            (await _organizaciones.GetQueryableAsync()).OrderBy(o => o.Nombre));

        return new OpcionesDeActividadDto
        {
            Personas = personas.Select(u => new PersonaDeActividadDto { Id = u.Id, Usuario = u.UserName, NombreCompleto = Nombre(u) }).ToList(),
            Organizaciones = organizaciones
                .Select(o => new OrganizacionResumenDto { Id = o.Id, Nombre = o.Nombre })
                .ToList(),
            DiasDeRegistro = MonitoringConsts.RetentionDays,
        };
    }

    public async Task<PaginaDeActividadDto> GetListAsync(GetActividadInput input)
    {
        var ahora = Clock.Now;
        var desde = input.Desde ?? ahora.AddDays(-MonitoringConsts.RetentionDays);
        var antesDe = input.AntesDe ?? ahora.AddMinutes(1);
        var ocultos = (await _jerarquia.UsuariosFueraDeAlcanceAsync()).ToHashSet();

        // A quién se mira. Con organización: sus miembros, y además lo que se hizo con lo
        // de esa organización (lo que hizo el equipo, por ejemplo).
        HashSet<Guid>? actores = null;
        HashSet<string>? permisosDeLaOrganizacion = null;
        if (input.UsuarioId.HasValue)
        {
            actores = [input.UsuarioId.Value];
        }

        if (input.OrganizacionId.HasValue)
        {
            var org = await AsyncExecuter.FirstOrDefaultAsync(
                (await _organizaciones.WithDetailsAsync(o => o.Miembros)).Where(o => o.Id == input.OrganizacionId.Value));
            var miembros = org?.Miembros.Select(m => m.UserId).ToHashSet() ?? [];
            actores = actores == null ? miembros : actores.Where(miembros.Contains).ToHashSet();
            permisosDeLaOrganizacion = org == null ? [] : [org.Id.ToString()];
        }

        // Filtrar por persona en la base solo si no hay que mirar también lo de la
        // organización (eso lo pudo tocar cualquiera del equipo).
        IReadOnlyCollection<Guid>? enLaBase = permisosDeLaOrganizacion == null ? actores : null;
        var cuantos = TamanoDePagina * 3;
        var tipo = input.Tipo;
        bool Quiero(string t) => string.IsNullOrWhiteSpace(tipo) || tipo == t;

        var acciones = Quiero(TipoDeActividad.Organizacion) || Quiero(TipoDeActividad.Personas)
            ? await _registro.AccionesAsync(desde, antesDe, Servicios, Metodos, enLaBase, cuantos)
            : [];
        var sesiones = Quiero(TipoDeActividad.Sesion)
            ? await _registro.SesionesAsync(desde, antesDe, AccionesDeSesion, enLaBase, cuantos)
            : [];

        List<Invitacion> aceptadas = [];
        using (_filtros.Disable<ISoftDelete>())
        {
            if (Quiero(TipoDeActividad.Personas))
            {
                aceptadas = await AsyncExecuter.ToListAsync((await _invitaciones.GetQueryableAsync())
                    .Where(i => i.UsuarioId != null && i.AceptadaEn >= desde && i.AceptadaEn < antesDe)
                    .OrderByDescending(i => i.AceptadaEn).Take(cuantos));
            }
        }

        var parametros = acciones.ToDictionary(a => a, a => Leer(a.Parametros));
        var nombres = await ReferenciasAsync(acciones, parametros, aceptadas, sesiones);

        var eventos = new List<Evento>();
        foreach (var a in acciones)
        {
            var e = DeAccion(a, parametros[a], nombres);
            if (e != null)
            {
                eventos.Add(e);
            }
        }

        eventos.AddRange(sesiones.Select(s => DeSesion(s)).Where(e => e != null)!);
        eventos.AddRange(aceptadas.Select(i => new Evento(i.AceptadaEn!.Value, i.UsuarioId!.Value, TipoDeActividad.Personas,
            i.CreatorId.HasValue && !ocultos.Contains(i.CreatorId.Value)
                ? $"Creó su cuenta con la invitación de {Usuario(nombres, i.CreatorId.Value)}"
                : "Creó su cuenta con una invitación",
            null, null)));

        var visibles = eventos
            .Where(e => !ocultos.Contains(e.UserId))
            .Where(e => string.IsNullOrWhiteSpace(tipo) || e.Tipo == tipo)
            .Where(e => actores == null
                        || actores.Contains(e.UserId)
                        || (permisosDeLaOrganizacion != null && e.Permiso != null && permisosDeLaOrganizacion.Contains(e.Permiso)))
            .OrderByDescending(e => e.Fecha)
            .ToList();

        var juntos = Juntar(visibles);
        var pagina = juntos.Take(TamanoDePagina).ToList();

        // Hay más si alguna fuente llegó a su tope o sobraron eventos tras cortar la página.
        var fuenteLlena = acciones.Count == cuantos || sesiones.Count == cuantos || aceptadas.Count == cuantos;
        var siguiente = pagina.Count > 0 && (juntos.Count > TamanoDePagina || fuenteLlena) ? pagina[^1].FechaMasAntigua : (DateTime?)null;

        var organizacionesDe = await OrganizacionesDeAsync(pagina.Select(e => e.UserId).Distinct().ToList());
        return new PaginaDeActividadDto
        {
            Items = pagina.Select(e => new ActividadDto
            {
                Fecha = e.Fecha,
                UsuarioId = e.UserId,
                Usuario = nombres.Usuarios.TryGetValue(e.UserId, out var u) ? u.UserName : "(cuenta borrada)",
                NombreCompleto = nombres.Usuarios.TryGetValue(e.UserId, out var u2) ? Nombre(u2) : null,
                Organizaciones = organizacionesDe.GetValueOrDefault(e.UserId) ?? [],
                Tipo = e.Tipo,
                Texto = e.Texto,
                Veces = e.Veces,
                Ip = e.Ip,
            }).ToList(),
            SiguienteAntesDe = siguiente,
        };
    }

    // --- Traducción a frases --------------------------------------------------------

    private static Evento? DeAccion(AccionRegistrada a, JsonElement p, Referencias n)
    {
        var id = Guid(p, "id");
        string? texto = null;
        var tipo = TipoDeActividad.Personas;

        if (a.Servicio == SvcOrganizaciones)
        {
            tipo = TipoDeActividad.Organizacion;
            var org = id.HasValue ? Organizacion(n, id.Value) : null;
            texto = a.Metodo switch
            {
                "CreateAsync" => $"Creó la organización «{Texto(p, "input", "nombre")}»",
                "UpdateAsync" => $"Editó la organización «{Texto(p, "input", "nombre") ?? org}»",
                "DeleteAsync" => $"Borró la organización «{org}»",
                "AgregarMiembroAsync" => $"Añadió a {Usuario(n, Guid(p, "input", "userId"))} a «{org}»",
                "QuitarMiembroAsync" => $"Quitó a {Usuario(n, Guid(p, "userId"))} de «{org}»",
                _ => null,
            };
        }
        else if (a.Servicio == SvcInvitaciones)
        {
            tipo = TipoDeActividad.Personas;
            var email = id.HasValue && n.Invitaciones.TryGetValue(id.Value, out var inv) ? inv : "(invitación)";
            texto = a.Metodo switch
            {
                "CreateAsync" => $"Invitó a {Texto(p, "input", "email")} como {AuxiliarRoles.Etiqueta(Texto(p, "input", "rol") ?? string.Empty)}",
                "ReenviarAsync" => $"Reenvió la invitación de {email}",
                "RevocarAsync" => $"Revocó la invitación de {email}",
                _ => null,
            };
        }
        else if (a.Servicio == SvcUsuarios)
        {
            tipo = TipoDeActividad.Personas;
            texto = a.Metodo switch
            {
                "CreateAsync" => $"Dio de alta a {Texto(p, "input", "userName")}",
                "UpdateAsync" => id == a.UserId ? "Editó sus propios datos" : $"Editó los datos de {Usuario(n, id)}",
                "DeleteAsync" => $"Borró la cuenta de {Usuario(n, id)}",
                _ => null,
            };
        }
        else if (a.Servicio == SvcSeguridad && a.Metodo == "QuitarSegundoFactorAsync")
        {
            tipo = TipoDeActividad.Personas;
            texto = $"Quitó la verificación en dos pasos a {Usuario(n, id)}";
        }

        if (texto == null)
        {
            return null;
        }

        // Lo que se hizo con una organización cuenta también al filtrar por ella.
        var organizacion = a.Servicio == SvcOrganizaciones ? id?.ToString() : null;
        return new Evento(a.Fecha, a.UserId, tipo, texto, a.Ip, organizacion);
    }

    private static Evento? DeSesion(SesionRegistrada s)
    {
        var texto = s.Accion switch
        {
            "LoginSucceeded" => "Entró a la consola",
            "LoginFailed" or "LoginInvalidUserNameOrPassword" => "Intentó entrar con una contraseña equivocada",
            "LoginLockedout" => "Su cuenta se bloqueó por intentos fallidos",
            "ChangePassword" => "Cambió su contraseña",
            "TwoFactorEnabled" => "Activó la verificación en dos pasos",
            "TwoFactorDisabled" => "Quitó la verificación en dos pasos",
            _ => null,
        };
        return texto == null ? null : new Evento(s.Fecha, s.UserId, TipoDeActividad.Sesion, texto, s.Ip, null);
    }

    /// <summary>
    /// Lo mismo, de la misma persona, seguido y en menos de media hora (abrir el mismo reporte
    /// cinco veces) se cuenta una vez con «×5». La lista llega de la más reciente a la más antigua.
    /// </summary>
    private static List<Evento> Juntar(List<Evento> eventos)
    {
        var salida = new List<Evento>();
        foreach (var e in eventos)
        {
            var previo = salida.LastOrDefault();
            if (previo != null && previo.UserId == e.UserId && previo.Texto == e.Texto
                && previo.FechaMasAntigua - e.Fecha <= VentanaDeRepeticion)
            {
                previo.Veces++;
                previo.FechaMasAntigua = e.Fecha;
                continue;
            }

            salida.Add(e);
        }

        return salida;
    }

    // --- Nombres de lo que se menciona ----------------------------------------------

    private sealed class Referencias
    {
        public Dictionary<Guid, IdentityUser> Usuarios { get; } = [];
        public Dictionary<Guid, string> Organizaciones { get; } = [];
        public Dictionary<Guid, string> Invitaciones { get; } = [];
    }

    /// <summary>
    /// Todo lo que las frases nombran, de una vez y con los BORRADOS incluidos: «borró la
    /// organización X» necesita el nombre de X aunque ya no exista.
    /// </summary>
    private async Task<Referencias> ReferenciasAsync(
        List<AccionRegistrada> acciones,
        Dictionary<AccionRegistrada, JsonElement> parametros,
        List<Invitacion> aceptadas,
        List<SesionRegistrada> sesiones)
    {
        var r = new Referencias();
        var idsOrg = acciones.Where(a => a.Servicio == SvcOrganizaciones).Select(a => Guid(parametros[a], "id"))
            .Where(g => g.HasValue).Select(g => g!.Value).ToHashSet();
        var idsInv = acciones.Where(a => a.Servicio == SvcInvitaciones).Select(a => Guid(parametros[a], "id"))
            .Where(g => g.HasValue).Select(g => g!.Value).ToHashSet();

        var idsUsuario = acciones.Select(a => a.UserId)
            .Concat(sesiones.Select(s => s.UserId))
            .Concat(aceptadas.Select(i => i.UsuarioId!.Value))
            .Concat(aceptadas.Where(i => i.CreatorId.HasValue).Select(i => i.CreatorId!.Value))
            .ToHashSet();
        foreach (var a in acciones.Where(a => a.Servicio is var s && (s == SvcUsuarios || s == SvcSeguridad || s == SvcOrganizaciones)))
        {
            foreach (var g in new[] { Guid(parametros[a], "id"), Guid(parametros[a], "userId"), Guid(parametros[a], "input", "userId") })
            {
                if (g.HasValue)
                {
                    idsUsuario.Add(g.Value);
                }
            }
        }

        using (_filtros.Disable<ISoftDelete>())
        {
            if (idsOrg.Count > 0)
            {
                foreach (var o in await _organizaciones.GetListAsync(x => idsOrg.Contains(x.Id)))
                {
                    r.Organizaciones[o.Id] = o.Nombre;
                }
            }

            if (idsInv.Count > 0)
            {
                foreach (var i in await _invitaciones.GetListAsync(x => idsInv.Contains(x.Id)))
                {
                    r.Invitaciones[i.Id] = i.Email;
                }
            }

            if (idsUsuario.Count > 0)
            {
                foreach (var u in await _usuarios.GetListAsync(x => idsUsuario.Contains(x.Id)))
                {
                    r.Usuarios[u.Id] = u;
                }
            }
        }

        return r;
    }

    private async Task<Dictionary<Guid, List<string>>> OrganizacionesDeAsync(List<Guid> usuarios)
    {
        if (usuarios.Count == 0)
        {
            return [];
        }

        var filas = await AsyncExecuter.ToListAsync((await _organizaciones.GetQueryableAsync())
            .SelectMany(o => o.Miembros.Where(m => usuarios.Contains(m.UserId)).Select(m => new { m.UserId, o.Nombre })));
        return filas.GroupBy(f => f.UserId).ToDictionary(g => g.Key, g => g.Select(f => f.Nombre).OrderBy(x => x).ToList());
    }

    private static string Organizacion(Referencias n, Guid id) =>
        n.Organizaciones.TryGetValue(id, out var o) ? o : "(organización)";

    private static string Usuario(Referencias n, Guid? id) =>
        id.HasValue && n.Usuarios.TryGetValue(id.Value, out var u) ? u.UserName : "(cuenta borrada)";

    private static string? Nombre(IdentityUser u)
    {
        var n = string.Join(' ', new[] { u.Name, u.Surname }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return n.Length > 0 ? n : null;
    }

    // --- Parámetros en JSON (como los guarda ABP) ----------------------------------

    private static JsonElement Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // ABP recorta los parámetros largos: un JSON cortado no rompe la pantalla.
            return default;
        }
    }

    private static JsonElement? Ruta(JsonElement raiz, params string[] ruta)
    {
        var actual = raiz;
        foreach (var paso in ruta)
        {
            if (actual.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var encontrada = actual.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, paso, StringComparison.OrdinalIgnoreCase));
            if (encontrada.Value.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            actual = encontrada.Value;
        }

        return actual;
    }

    private static string? Texto(JsonElement raiz, params string[] ruta) =>
        Ruta(raiz, ruta) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static Guid? Guid(JsonElement raiz, params string[] ruta) =>
        System.Guid.TryParse(Texto(raiz, ruta), out var g) ? g : null;

    private sealed class Evento(DateTime fecha, Guid userId, string tipo, string texto, string? ip, string? permiso)
    {
        public DateTime Fecha { get; } = fecha;
        public DateTime FechaMasAntigua { get; set; } = fecha;
        public Guid UserId { get; } = userId;
        public string Tipo { get; } = tipo;
        public string Texto { get; } = texto;
        public string? Ip { get; } = ip;
        public string? Permiso { get; } = permiso;
        public int Veces { get; set; } = 1;
    }
}
