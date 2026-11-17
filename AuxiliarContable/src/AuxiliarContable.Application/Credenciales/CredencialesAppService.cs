using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AuxiliarContable.Credenciales;

/// <summary>
/// Ver <see cref="ICredencialesAppService"/>.
///
/// <para><b>[DisableAuditing]</b> en toda la clase: el registro de auditoría de ABP guarda los parámetros de cada
/// petición, y aquí llegan contraseñas, NIP y archivos de llave. Lo que importa (quién vio o cambió qué) queda en la
/// bitácora propia (<see cref="UsoDeCredencial"/>), sin el dato.</para>
/// </summary>
[Authorize]
[DisableAuditing]
public class CredencialesAppService : AuxiliarContableAppService, ICredencialesAppService
{
    private const string EquipoTecnico = "Equipo técnico";

    private readonly IRepository<CredencialesDelCliente, Guid> _credenciales;
    private readonly IRepository<NipDeUsuario, Guid> _nips;
    private readonly IRepository<UsoDeCredencial, Guid> _usos;
    private readonly IRepository<IdentityUser, Guid> _usuarios;
    private readonly CifradorDeCredenciales _cifrador;
    private readonly Autorizador _autorizador;
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly IdentityUserManager _cuentas;

    public CredencialesAppService(
        IRepository<CredencialesDelCliente, Guid> credenciales,
        IRepository<NipDeUsuario, Guid> nips,
        IRepository<UsoDeCredencial, Guid> usos,
        IRepository<IdentityUser, Guid> usuarios,
        CifradorDeCredenciales cifrador,
        Autorizador autorizador,
        JerarquiaDeUsuarios jerarquia,
        IdentityUserManager cuentas)
    {
        _credenciales = credenciales;
        _nips = nips;
        _usos = usos;
        _usuarios = usuarios;
        _cifrador = cifrador;
        _autorizador = autorizador;
        _jerarquia = jerarquia;
        _cuentas = cuentas;
    }

    public async Task<CredencialesDto> GetAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        return await DescribirAsync(organizacionId, await _credenciales.FindAsync(c => c.OrganizacionId == organizacionId));
    }

    public async Task<CredencialesDto> GuardarEfirmaAsync(Guid organizacionId, GuardarEfirmaDto input)
    {
        await ExigirAdministrarAsync(organizacionId);
        var cer = Archivo(input.CerBase64, ".cer");
        var key = Archivo(input.KeyBase64, ".key");
        var datos = LectorDeEfirma.LeerCer(cer);
        LectorDeEfirma.ComprobarLlave(cer, key, input.Contrasena);

        var c = await ObtenerOCrearAsync(organizacionId, input.Consentimiento);
        c.PonerEfirma(
            _cifrador.Cifrar(c.Id, CredencialesDelCliente.CampoCer, cer),
            _cifrador.Cifrar(c.Id, CredencialesDelCliente.CampoKey, key),
            _cifrador.CifrarTexto(c.Id, CredencialesDelCliente.CampoContrasenaDeEfirma, input.Contrasena),
            datos, Clock.Now.ToUniversalTime());
        await _credenciales.UpdateAsync(c, autoSave: true);
        await AnotarAsync(organizacionId, TipoDeCredencial.Efirma, AccionSobreCredencial.Guardar);
        return await DescribirAsync(organizacionId, c);
    }

    public async Task<CredencialesDto> GuardarCiecAsync(Guid organizacionId, GuardarCiecDto input)
    {
        await ExigirAdministrarAsync(organizacionId);
        var rfc = (input.Rfc ?? "").Trim().ToUpperInvariant();
        if (rfc.Length is < 12 or > 13)
        {
            throw new UserFriendlyException("El RFC tiene 12 caracteres (persona moral) o 13 (persona física).");
        }

        var c = await ObtenerOCrearAsync(organizacionId, input.Consentimiento);
        c.PonerCiec(rfc, _cifrador.CifrarTexto(c.Id, CredencialesDelCliente.CampoCiec, input.Contrasena), Clock.Now.ToUniversalTime());
        await _credenciales.UpdateAsync(c, autoSave: true);
        await AnotarAsync(organizacionId, TipoDeCredencial.Ciec, AccionSobreCredencial.Guardar);
        return await DescribirAsync(organizacionId, c);
    }

    public async Task<CredencialesDto> BorrarAsync(Guid organizacionId, TipoDeCredencial tipo)
    {
        await ExigirAdministrarAsync(organizacionId);
        var c = await _credenciales.FindAsync(x => x.OrganizacionId == organizacionId);
        if (c is not null)
        {
            c.Borrar(tipo);
            await _credenciales.UpdateAsync(c, autoSave: true);
            await AnotarAsync(organizacionId, tipo, AccionSobreCredencial.Borrar);
        }

        return await DescribirAsync(organizacionId, c);
    }

    public async Task<ContrasenaReveladaDto> RevelarAsync(Guid organizacionId, RevelarCredencialDto input)
    {
        await ExigirAdministrarAsync(organizacionId);
        await VerificarAsync(input);
        var c = await ExistentesAsync(organizacionId);
        var contrasena = input.Tipo == TipoDeCredencial.Efirma
            ? Texto(c.Id, CredencialesDelCliente.CampoContrasenaDeEfirma, c.ContrasenaDeEfirmaCifrada, "e.firma")
            : Texto(c.Id, CredencialesDelCliente.CampoCiec, c.CiecCifrada, "CIEC");
        await AnotarAsync(organizacionId, input.Tipo, AccionSobreCredencial.VerContrasena);
        return new ContrasenaReveladaDto { Contrasena = contrasena };
    }

    public async Task<ArchivoDeCredencialDto> DescargarAsync(Guid organizacionId, DescargarCredencialDto input)
    {
        await ExigirAdministrarAsync(organizacionId);
        var esKey = input.Archivo == "key";
        if (esKey)
        {
            await VerificarAsync(input);
        }

        var c = await ExistentesAsync(organizacionId);
        var cifrado = (esKey ? c.KeyCifrado : c.CerCifrado)
                      ?? throw new UserFriendlyException("Este cliente no tiene e.firma guardada.");
        var bytes = _cifrador.Descifrar(c.Id, esKey ? CredencialesDelCliente.CampoKey : CredencialesDelCliente.CampoCer, cifrado);
        await AnotarAsync(organizacionId, TipoDeCredencial.Efirma, esKey ? AccionSobreCredencial.DescargarKey : AccionSobreCredencial.DescargarCer);
        var nombre = $"{c.EfirmaRfc ?? "efirma"}{(c.NumeroDeCertificado is { } n ? "_" + n : "")}.{input.Archivo}";
        return new ArchivoDeCredencialDto { Nombre = nombre, ContenidoBase64 = Convert.ToBase64String(bytes) };
    }

    public async Task PonerNipAsync(PonerNipDto input)
    {
        var yo = CurrentUser.GetId();
        var usuario = await _cuentas.GetByIdAsync(yo);
        if (!await _cuentas.CheckPasswordAsync(usuario, input.ContrasenaDeCuenta))
        {
            throw new UserFriendlyException("La contraseña de tu cuenta no es correcta.");
        }

        if (!NipDeUsuario.EsValido(input.Nip))
        {
            throw new UserFriendlyException("El NIP son 4 dígitos.");
        }

        var nip = await _nips.FindAsync(n => n.UsuarioId == yo);
        if (nip is null)
        {
            nip = new NipDeUsuario(GuidGenerator.Create(), yo);
            nip.Poner(input.Nip);
            await _nips.InsertAsync(nip, autoSave: true);
        }
        else
        {
            nip.Poner(input.Nip);
            await _nips.UpdateAsync(nip, autoSave: true);
        }
    }

    public async Task<List<UsoDeCredencialDto>> GetBitacoraAsync(Guid organizacionId)
    {
        await ExigirAdministrarAsync(organizacionId);
        var usos = await AsyncExecuter.ToListAsync(
            (await _usos.GetQueryableAsync())
            .Where(u => u.OrganizacionId == organizacionId)
            .OrderByDescending(u => u.CreationTime)
            .Take(50));
        var ocultos = (await _jerarquia.DevelopersOcultosAsync()).ToHashSet();
        var ids = usos.Where(u => u.CreatorId.HasValue).Select(u => u.CreatorId!.Value).Distinct().ToList();
        var nombres = (await AsyncExecuter.ToListAsync((await _usuarios.GetQueryableAsync()).Where(u => ids.Contains(u.Id))))
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.Name) ? u.UserName : $"{u.Name} {u.Surname}".Trim());
        return usos.Select(u => new UsoDeCredencialDto
        {
            Cuando = u.CreationTime,
            Quien = u.CreatorId is not { } id ? "—" : ocultos.Contains(id) ? EquipoTecnico : nombres.GetValueOrDefault(id, "—"),
            Tipo = u.Tipo,
            Accion = u.Accion,
        }).ToList();
    }

    /// <summary>
    /// Ver contraseñas, descargar, guardar y borrar: el equipo del despacho y el administrador de la organización del
    /// cliente. Quien solo ve o sube documentos del cliente ve la vigencia, no las contraseñas.
    /// </summary>
    private async Task ExigirAdministrarAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (!(await _autorizador.QuienMiraAsync()).Puede(organizacionId, AccionEnOrganizacion.GestionarPersonas))
        {
            throw new UserFriendlyException("Solo el administrador de la organización y el despacho ven y cambian las credenciales.");
        }
    }

    /// <summary>NIP o contraseña de la cuenta. Cinco fallos seguidos (de cualquiera de los dos) bloquean 15 minutos.</summary>
    private async Task VerificarAsync(VerificacionDto input)
    {
        var yo = CurrentUser.GetId();
        var ahora = Clock.Now.ToUniversalTime();
        var nip = await _nips.FindAsync(n => n.UsuarioId == yo);
        if (nip is null)
        {
            nip = new NipDeUsuario(GuidGenerator.Create(), yo);
            await _nips.InsertAsync(nip, autoSave: true);
        }

        if (nip.Bloqueado(ahora))
        {
            throw new UserFriendlyException(
                $"Demasiados intentos. Espera {CredencialesConsts.MinutosDeBloqueo} minutos e inténtalo de nuevo.");
        }

        bool bien;
        if (!string.IsNullOrEmpty(input.Nip))
        {
            bien = nip.Coincide(input.Nip);
        }
        else if (!string.IsNullOrEmpty(input.ContrasenaDeCuenta))
        {
            bien = await _cuentas.CheckPasswordAsync(await _cuentas.GetByIdAsync(yo), input.ContrasenaDeCuenta);
        }
        else
        {
            throw new UserFriendlyException(nip.TieneNip ? "Escribe tu NIP." : "Escribe la contraseña de tu cuenta (o pon un NIP).");
        }

        if (bien)
        {
            nip.Acierto();
        }
        else
        {
            nip.Fallo(ahora);
            Logger.LogWarning("Verificación de credenciales fallida para el usuario {Usuario}", yo);
        }

        await _nips.UpdateAsync(nip, autoSave: true);
        if (!bien)
        {
            throw new UserFriendlyException(nip.Bloqueado(ahora)
                ? $"Demasiados intentos. Espera {CredencialesConsts.MinutosDeBloqueo} minutos e inténtalo de nuevo."
                : !string.IsNullOrEmpty(input.Nip) ? "El NIP no es correcto." : "La contraseña no es correcta.");
        }
    }

    private async Task<CredencialesDelCliente> ObtenerOCrearAsync(Guid organizacionId, bool consentimiento)
    {
        var c = await _credenciales.FindAsync(x => x.OrganizacionId == organizacionId);
        if (c is null)
        {
            c = new CredencialesDelCliente(GuidGenerator.Create(), organizacionId);
            await _credenciales.InsertAsync(c, autoSave: true);
        }

        if (consentimiento)
        {
            c.RegistrarConsentimiento(CurrentUser.Id, Clock.Now.ToUniversalTime());
        }

        return c;
    }

    private async Task<CredencialesDelCliente> ExistentesAsync(Guid organizacionId) =>
        await _credenciales.FindAsync(x => x.OrganizacionId == organizacionId)
        ?? throw new UserFriendlyException("Este cliente no tiene credenciales guardadas.");

    private string Texto(Guid id, string campo, byte[]? cifrado, string que) =>
        cifrado is null
            ? throw new UserFriendlyException($"Este cliente no tiene {que} guardada.")
            : _cifrador.DescifrarTexto(id, campo, cifrado);

    private static byte[] Archivo(string base64, string extension)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64 ?? "");
        }
        catch (FormatException)
        {
            throw new UserFriendlyException($"El archivo {extension} no llegó bien. Vuelve a elegirlo.");
        }

        return bytes.Length is > 0 and <= CredencialesConsts.MaxBytesDeArchivo
            ? bytes
            : throw new UserFriendlyException($"El archivo {extension} está vacío o es demasiado grande para ser de la e.firma.");
    }

    private Task AnotarAsync(Guid organizacionId, TipoDeCredencial tipo, AccionSobreCredencial accion) =>
        _usos.InsertAsync(new UsoDeCredencial(GuidGenerator.Create(), organizacionId, tipo, accion), autoSave: true);

    private async Task<CredencialesDto> DescribirAsync(Guid organizacionId, CredencialesDelCliente? c)
    {
        var quien = await _autorizador.QuienMiraAsync();
        var hoy = Clock.Now.ToUniversalTime().Date;
        var nip = await _nips.FindAsync(n => n.UsuarioId == quien.Yo);
        return new CredencialesDto
        {
            OrganizacionId = organizacionId,
            ConsentimientoUtc = c?.ConsentimientoUtc,
            PuedeAdministrar = quien.Puede(organizacionId, AccionEnOrganizacion.GestionarPersonas),
            TieneNip = nip?.TieneNip == true,
            Efirma = c is { TieneEfirma: true, VigenteHastaUtc: { } hasta }
                ? new EfirmaDto
                {
                    Rfc = c.EfirmaRfc,
                    NumeroDeCertificado = c.NumeroDeCertificado,
                    VigenteDesdeUtc = c.VigenteDesdeUtc!.Value,
                    VigenteHastaUtc = hasta,
                    DiasParaVencer = (int)Math.Floor((hasta.Date - hoy).TotalDays),
                    PorVencer = (hasta.Date - hoy).TotalDays <= CredencialesConsts.DiasDeAviso,
                    ActualizadaUtc = c.EfirmaActualizadaUtc!.Value,
                }
                : null,
            Ciec = c is { TieneCiec: true } ? new CiecDto { Rfc = c.CiecRfc!, ActualizadaUtc = c.CiecActualizadaUtc!.Value } : null,
        };
    }
}
