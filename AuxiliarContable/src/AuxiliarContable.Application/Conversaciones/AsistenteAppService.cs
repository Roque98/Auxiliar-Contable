using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.IA;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.Settings;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Identity;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Settings;
using Volo.Abp.Users;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// Ver <see cref="IAsistenteAppService"/>. Ver la conversación pide <see cref="AccionEnOrganizacion.Ver"/>;
/// las herramientas que cambian datos solo se le ofrecen al modelo si quien escribe tiene
/// <see cref="AccionEnOrganizacion.Subir"/> (y el servicio que llaman lo vuelve a exigir).
///
/// <para>La llamada al modelo va DENTRO de la petición, a diferencia de leer documentos: es una
/// plática y la persona espera la respuesta. Si el modelo falla, no se guarda nada del turno
/// (ni el mensaje de la persona) y la persona lo vuelve a mandar.</para>
/// </summary>
[Authorize]
public class AsistenteAppService : AuxiliarContableAppService, IAsistenteAppService
{
    private readonly IRepository<Conversacion, Guid> _conversaciones;
    private readonly IRepository<MensajeDeConversacion, Guid> _mensajes;
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly Autorizador _autorizador;
    private readonly IModeloConversacional _modelo;
    private readonly HerramientasDelAsistente _herramientas;

    public AsistenteAppService(
        IRepository<Conversacion, Guid> conversaciones,
        IRepository<MensajeDeConversacion, Guid> mensajes,
        IRepository<Organizacion, Guid> organizaciones,
        Autorizador autorizador,
        IModeloConversacional modelo,
        HerramientasDelAsistente herramientas)
    {
        _conversaciones = conversaciones;
        _mensajes = mensajes;
        _organizaciones = organizaciones;
        _autorizador = autorizador;
        _modelo = modelo;
        _herramientas = herramientas;
    }

    public async Task<ConversacionDto> GetAsync(Guid organizacionId)
    {
        await ExigirVerAsync(organizacionId);
        return await DescribirAsync(organizacionId, await AbiertaAsync(organizacionId));
    }

    public async Task<ConversacionDto> EmpezarDeNuevoAsync(Guid organizacionId)
    {
        await ExigirVerAsync(organizacionId);
        if (await AbiertaAsync(organizacionId) is { } abierta)
        {
            abierta.Cerrar();
            await _conversaciones.UpdateAsync(abierta, autoSave: true);
        }

        return await DescribirAsync(organizacionId, null);
    }

    public async Task<ConversacionDto> EnviarAsync(Guid organizacionId, EnviarMensajeDto input)
    {
        await ExigirVerAsync(organizacionId);
        var texto = (input.Texto ?? string.Empty).Trim();
        if (texto.Length == 0)
        {
            throw new UserFriendlyException("Escribe un mensaje.");
        }

        if (texto.Length > ConversacionConsts.MaxTextoDelUsuario)
        {
            throw new UserFriendlyException($"El mensaje es muy largo (máximo {ConversacionConsts.MaxTextoDelUsuario} caracteres).");
        }

        if (!await ChatEncendidoAsync())
        {
            throw new UserFriendlyException("El asistente no está disponible por ahora.");
        }

        var yo = CurrentUser.GetId();
        await ExigirCupoAsync(yo);

        var quien = await _autorizador.QuienMiraAsync();
        var puedeEditar = organizacionId != Guid.Empty && quien.Puede(organizacionId, AccionEnOrganizacion.Subir);
        var conversacion = await AbiertaAsync(organizacionId)
                           ?? await _conversaciones.InsertAsync(new Conversacion(GuidGenerator.Create(), yo, organizacionId, Clock.Now), autoSave: true);

        var historial = await _mensajes.GetListAsync(m => m.ConversacionId == conversacion.Id);
        var numero = historial.Count == 0 ? 0 : historial.Max(m => m.Numero);

        // Los mensajes del turno se guardan al FINAL, todos juntos: si el modelo falla a medias no
        // queda una conversación con la pregunta y sin respuesta (o con un uso de herramienta sin
        // su resultado, que el modelo rechazaría en la siguiente llamada).
        var nuevos = new List<MensajeDeConversacion>();

        Task GuardarAsync(RolDelMensaje rol, string visible, IReadOnlyList<BloqueDeTurno> bloques, int tokens = 0)
        {
            var mensaje = new MensajeDeConversacion(GuidGenerator.Create(), conversacion.Id, ++numero, rol, visible, bloques, Clock.Now, tokens);
            historial.Add(mensaje);
            nuevos.Add(mensaje);
            return Task.CompletedTask;
        }

        await GuardarAsync(RolDelMensaje.Usuario, texto, new BloqueDeTurno[] { new BloqueDeTexto(texto) });

        // Invitar desde el chat: el equipo con permiso de dar de alta personas, sobre un cliente.
        var puedeInvitar = organizacionId != Guid.Empty && quien.Equipo
                           && await AuthorizationService.IsGrantedAsync(IdentityPermissions.Users.Create);
        var instrucciones = await InstruccionesAsync(organizacionId, quien.Equipo, puedeEditar, puedeInvitar);
        var herramientas = organizacionId == Guid.Empty
            ? HerramientasDelAsistente.DeCartera()
            : HerramientasDelAsistente.Disponibles(puedeEditar, puedeInvitar, quien.Equipo);

        var verificada = false;
        for (var vuelta = 1; ; vuelta++)
        {
            RespuestaDeConversacion respuesta;
            try
            {
                respuesta = await _modelo.ConversarAsync(
                    new SolicitudDeConversacion("chat", instrucciones, Turnos(historial), herramientas));
            }
            catch (ErrorDeIAException ex)
            {
                Logger.LogWarning("Chat: el modelo falló ({Motivo}): {Mensaje}", ex.Motivo, ex.Message);
                throw new UserFriendlyException(ex.EsReintentable
                    ? "El asistente está saturado en este momento. Intenta de nuevo en un minuto."
                    : "El asistente no pudo responder. Intenta de nuevo o escribe de otra forma.");
            }

            var usos = respuesta.MotivoDeParada == "max_tokens" ? [] : respuesta.UsosDeHerramientas.ToList();
            var bloques = usos.Count == 0
                ? respuesta.Bloques.Where(b => b is BloqueDeTexto).ToList()
                : respuesta.Bloques;
            if (bloques.Count == 0)
            {
                bloques = [new BloqueDeTexto("…")];
            }

            // Antes de enseñar la respuesta: lo que cita tiene que venir en lo que consultó en este turno. Una vez; el borrador
            // queda guardado sin texto visible (no se le enseña a la persona) y el modelo recibe el aviso.
            if (usos.Count == 0 && !verificada && vuelta < ConversacionConsts.MaxVueltasDeHerramientas)
            {
                var consultado = nuevos.Where(m => m.Rol == RolDelMensaje.Herramientas)
                    .SelectMany(m => m.ATurno().Bloques.OfType<BloqueDeResultadoDeHerramienta>().Select(r => r.Contenido));
                var faltan = VerificadorDeCitas.SinRespaldo(respuesta.Texto, consultado);
                if (faltan.Count > 0)
                {
                    verificada = true;
                    Logger.LogInformation("Chat: {Citas} citas sin respaldo; se le pide verificarlas.", faltan.Count);
                    await GuardarAsync(RolDelMensaje.Asistente, string.Empty, bloques,
                        respuesta.Uso.TokensDeEntrada + respuesta.Uso.TokensDeSalida);
                    await GuardarAsync(RolDelMensaje.Herramientas, string.Empty,
                        new BloqueDeTurno[] { new BloqueDeTexto(VerificadorDeCitas.Aviso(faltan, respuesta.Texto)) });
                    continue;
                }
            }

            await GuardarAsync(RolDelMensaje.Asistente, respuesta.Texto, bloques,
                respuesta.Uso.TokensDeEntrada + respuesta.Uso.TokensDeSalida);

            if (usos.Count == 0)
            {
                break;
            }

            // Las consultas que solo van al motor (leyes, sentencias, tarifa) corren a la vez; las que tocan la base, una por una.
            var resultados = new List<BloqueDeTurno>();
            if (usos.All(u => HerramientasDelAsistente.SoloConsultaAlMotor(u.Nombre)))
            {
                resultados.AddRange(await Task.WhenAll(usos.Select(u => _herramientas.EjecutarAsync(organizacionId, u))));
            }
            else
            {
                foreach (var uso in usos)
                {
                    resultados.Add(await _herramientas.EjecutarAsync(organizacionId, uso));
                }
            }

            await GuardarAsync(RolDelMensaje.Herramientas, string.Empty, resultados);

            if (vuelta >= ConversacionConsts.MaxVueltasDeHerramientas)
            {
                Logger.LogWarning("Chat: se cortó tras {Vueltas} vueltas de herramientas.", vuelta);
                await GuardarAsync(RolDelMensaje.Asistente, "No pude terminar esto. ¿Me lo puedes pedir de otra forma?",
                    new BloqueDeTurno[] { new BloqueDeTexto("No pude terminar esto. ¿Me lo puedes pedir de otra forma?") });
                break;
            }
        }

        await _mensajes.InsertManyAsync(nuevos, autoSave: true);
        conversacion.Tocar(Clock.Now);
        await _conversaciones.UpdateAsync(conversacion, autoSave: true);
        return await DescribirAsync(organizacionId, conversacion);
    }

    /// <summary>
    /// La conversación como turnos para el modelo. El turno en curso va completo (sus búsquedas y resultados);
    /// los anteriores, solo lo que se dijo: la pregunta y la respuesta que vio la persona. Sin sus búsquedas ni
    /// borradores, porque con ellos el modelo retomaba la investigación anterior en lugar de contestar la pregunta
    /// nueva, y cada vuelta reenviaba decenas de miles de caracteres (más lento). Si necesita algo de antes, lo vuelve a consultar.
    /// </summary>
    public static List<TurnoDeConversacion> Turnos(IEnumerable<MensajeDeConversacion> historial)
    {
        var todos = historial.OrderBy(m => m.Numero).ToList();
        var actual = todos.FindLastIndex(m => m.Rol == RolDelMensaje.Usuario);
        if (actual < 0)
        {
            return [];
        }

        var antes = new List<TurnoDeConversacion>();
        for (var i = 0; i < actual; i++)
        {
            if (todos[i].Rol != RolDelMensaje.Usuario)
            {
                continue;
            }

            var siguiente = todos.FindIndex(i + 1, m => m.Rol == RolDelMensaje.Usuario);
            var respuesta = todos.Skip(i + 1).Take(siguiente - i - 1)
                .LastOrDefault(m => m.Rol == RolDelMensaje.Asistente && !string.IsNullOrWhiteSpace(m.Texto));
            antes.Add(new TurnoDeConversacion(RolDelTurno.Usuario, [new BloqueDeTexto(todos[i].Texto)]));
            antes.Add(new TurnoDeConversacion(RolDelTurno.Asistente, [new BloqueDeTexto(respuesta?.Texto ?? "(Sin respuesta.)")]));
        }

        // Pares completos (pregunta y respuesta), los más recientes.
        var desde = Math.Max(0, antes.Count - ConversacionConsts.MensajesDeHistorial);
        desde += desde % 2;
        return antes.Skip(desde).Concat(todos.Skip(actual).Select(m => m.ATurno())).ToList();
    }

    private async Task ExigirCupoAsync(Guid yo)
    {
        var desde = Clock.Now.AddDays(-1);
        var conversaciones = await _conversaciones.GetQueryableAsync();
        var mensajes = await _mensajes.GetQueryableAsync();
        var hoy = await AsyncExecuter.CountAsync(
            from m in mensajes
            join c in conversaciones on m.ConversacionId equals c.Id
            where c.UsuarioId == yo && m.Rol == RolDelMensaje.Usuario && m.CreadoUtc >= desde
            select m.Id);
        if (hoy >= ConversacionConsts.MaxMensajesPorDia)
        {
            throw new UserFriendlyException("Llegaste al límite de mensajes del día. Mañana seguimos.");
        }
    }

    private async Task<Conversacion?> AbiertaAsync(Guid organizacionId)
    {
        var yo = CurrentUser.GetId();
        var consulta = (await _conversaciones.GetQueryableAsync())
            .Where(c => c.UsuarioId == yo && c.OrganizacionId == organizacionId && !c.Cerrada)
            .OrderByDescending(c => c.UltimoMensajeUtc);
        return await AsyncExecuter.FirstOrDefaultAsync(consulta);
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> es la conversación de TODA LA CARTERA: solo para el equipo, y en ella el
    /// modelo solo ve el resumen de la cartera (<see cref="HerramientasDelAsistente.DeCartera"/>), nunca el
    /// detalle de un cliente. Para eso se elige el cliente.
    /// </summary>
    private async Task ExigirVerAsync(Guid organizacionId)
    {
        if (organizacionId == Guid.Empty)
        {
            if (!(await _autorizador.QuienMiraAsync()).Equipo)
            {
                throw new EntityNotFoundException(typeof(Organizacion), organizacionId);
            }

            return;
        }

        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
    }

    private async Task<string> NombreAsync(Guid organizacionId) =>
        organizacionId == Guid.Empty ? NombreDeLaCartera : (await _organizaciones.GetAsync(organizacionId)).Nombre;

    public const string NombreDeLaCartera = "Toda la cartera";

    private async Task<string> InstruccionesAsync(Guid organizacionId, bool delDespacho, bool puedeEditar, bool puedeInvitar) =>
        organizacionId == Guid.Empty
            ? InstruccionesDelAsistente.ConstruirParaLaCartera(Clock.Now)
            : InstruccionesDelAsistente.Construir(await NombreAsync(organizacionId), Clock.Now, delDespacho, puedeEditar, puedeInvitar);

    /// <summary>Encendido en la configuración (AuxiliarContableSettings.AsistenteHabilitado) y con API key de Claude.</summary>
    private async Task<bool> ChatEncendidoAsync() =>
        _modelo.EstaConfigurado && await SettingProvider.IsTrueAsync(AuxiliarContableSettings.AsistenteHabilitado);

    private async Task<ConversacionDto> DescribirAsync(Guid organizacionId, Conversacion? conversacion)
    {
        var quien = await _autorizador.QuienMiraAsync();
        var dto = new ConversacionDto
        {
            OrganizacionId = organizacionId,
            NombreDelCliente = await NombreAsync(organizacionId),
            Disponible = await ChatEncendidoAsync(),
            PuedeEditar = organizacionId != Guid.Empty && quien.Puede(organizacionId, AccionEnOrganizacion.Subir),
            EsDelDespacho = quien.Equipo,
        };

        if (conversacion == null)
        {
            return dto;
        }

        var mensajes = (await _mensajes.GetListAsync(m => m.ConversacionId == conversacion.Id)).OrderBy(m => m.Numero);
        // Lo que hizo el asistente, por id del uso. Un uso cuyo resultado fue error NO se enseña:
        // «Dio de alta al cliente» con un nombre repetido sería mentira.
        var acciones = new List<(string Id, string Texto)>();
        foreach (var m in mensajes)
        {
            if (m.Rol == RolDelMensaje.Asistente)
            {
                acciones.AddRange(m.ATurno().Bloques.OfType<BloqueDeUsoDeHerramienta>()
                    .Select(u => (u.Id, Texto: HerramientasDelAsistente.Describir(u)))
                    .Where(a => a.Texto is not null)
                    .Select(a => (a.Id, a.Texto!)));
            }
            else if (m.Rol == RolDelMensaje.Herramientas)
            {
                var fallidos = m.ATurno().Bloques.OfType<BloqueDeResultadoDeHerramienta>().Where(r => r.EsError).Select(r => r.IdDelUso).ToHashSet();
                acciones.RemoveAll(a => fallidos.Contains(a.Id));
            }

            if (m.Rol == RolDelMensaje.Herramientas || string.IsNullOrWhiteSpace(m.Texto))
            {
                continue;
            }

            dto.Mensajes.Add(new MensajeDto
            {
                Id = m.Id,
                Rol = m.Rol,
                Texto = m.Texto,
                CreadoUtc = m.CreadoUtc,
                Acciones = m.Rol == RolDelMensaje.Asistente ? acciones.Select(a => a.Texto).Distinct().ToList() : [],
            });
            if (m.Rol == RolDelMensaje.Asistente)
            {
                acciones = [];
            }
        }

        return dto;
    }
}
