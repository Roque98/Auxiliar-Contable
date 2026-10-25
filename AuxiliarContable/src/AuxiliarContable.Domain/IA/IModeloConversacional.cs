using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.IA;

/// <summary>
/// El modelo hablando con una persona: el chat del cliente y el del despacho (CLAUDE.md §1.2.11).
///
/// <para>A diferencia de <see cref="IModeloDeIA"/>, aquí sí hay texto libre, pero el modelo NO
/// toca datos por su cuenta: todo lo que lee o guarda pasa por herramientas que ejecuta el
/// código, con los mismos permisos que tendría el usuario en la pantalla. Una llamada es UN
/// turno del modelo; el bucle de herramientas lo lleva quien llama.</para>
/// </summary>
public interface IModeloConversacional
{
    bool EstaConfigurado { get; }

    Task<RespuestaDeConversacion> ConversarAsync(SolicitudDeConversacion solicitud, CancellationToken cancellationToken = default);
}

/// <summary>Sin host (DbMigrator, tests) no hay modelo: cualquier uso lanza.</summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IModeloConversacional))]
public class ModeloConversacionalSinConfigurar : IModeloConversacional, ISingletonDependency
{
    public bool EstaConfigurado => false;

    public Task<RespuestaDeConversacion> ConversarAsync(SolicitudDeConversacion solicitud, CancellationToken cancellationToken = default)
    {
        throw new ErrorDeIAException(MotivoDeErrorDeIA.NoConfigurado, "No hay modelo de IA en este proceso.");
    }
}

public sealed class SolicitudDeConversacion
{
    public SolicitudDeConversacion(
        string proposito,
        string instrucciones,
        IReadOnlyList<TurnoDeConversacion> turnos,
        IReadOnlyList<HerramientaDelModelo>? herramientas = null)
    {
        if (string.IsNullOrWhiteSpace(proposito))
        {
            throw new ArgumentException("Falta el propósito.", nameof(proposito));
        }

        if (string.IsNullOrWhiteSpace(instrucciones))
        {
            throw new ArgumentException("Faltan las instrucciones.", nameof(instrucciones));
        }

        if (turnos is null || turnos.Count == 0 || turnos[0].Rol != RolDelTurno.Usuario)
        {
            throw new ArgumentException("La conversación tiene que empezar con un turno del usuario.", nameof(turnos));
        }

        Proposito = proposito;
        Instrucciones = instrucciones;
        Turnos = turnos;
        Herramientas = herramientas ?? Array.Empty<HerramientaDelModelo>();
    }

    public string Proposito { get; }

    /// <summary>Mensaje de sistema; se marca para la caché de prompts.</summary>
    public string Instrucciones { get; }

    public IReadOnlyList<TurnoDeConversacion> Turnos { get; }

    public IReadOnlyList<HerramientaDelModelo> Herramientas { get; }

    public int? MaxTokens { get; init; }
}

public enum RolDelTurno
{
    Usuario,
    Asistente,
}

public sealed record TurnoDeConversacion(RolDelTurno Rol, IReadOnlyList<BloqueDeTurno> Bloques)
{
    public static TurnoDeConversacion DelUsuario(params BloqueDeTurno[] bloques) => new(RolDelTurno.Usuario, bloques);

    public static TurnoDeConversacion DelUsuario(string texto) => new(RolDelTurno.Usuario, new BloqueDeTurno[] { new BloqueDeTexto(texto) });

    public static TurnoDeConversacion DelAsistente(IReadOnlyList<BloqueDeTurno> bloques) => new(RolDelTurno.Asistente, bloques);
}

/// <summary>
/// Se guarda tal cual en la base (<c>MensajeDeConversacion.Bloques</c>) para poder repetirle el
/// historial al modelo: por eso lleva discriminador y sus nombres NO se cambian.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(BloqueDeTexto), "texto")]
[JsonDerivedType(typeof(BloqueDeUsoDeHerramienta), "uso")]
[JsonDerivedType(typeof(BloqueDeResultadoDeHerramienta), "resultado")]
public abstract record BloqueDeTurno
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serializar(IReadOnlyList<BloqueDeTurno> bloques) => JsonSerializer.Serialize(bloques, Json);

    public static IReadOnlyList<BloqueDeTurno> Deserializar(string json) =>
        JsonSerializer.Deserialize<List<BloqueDeTurno>>(json, Json) ?? [];
}

public sealed record BloqueDeTexto(string Texto) : BloqueDeTurno;

/// <summary>El modelo pide ejecutar una herramienta. <see cref="Entrada"/> es el JSON tal cual lo mandó.</summary>
public sealed record BloqueDeUsoDeHerramienta(string Id, string Nombre, JsonElement Entrada) : BloqueDeTurno;

/// <summary>Lo que devolvió la herramienta, en un turno del usuario.</summary>
public sealed record BloqueDeResultadoDeHerramienta(string IdDelUso, string Contenido, bool EsError = false) : BloqueDeTurno;

/// <summary>Una herramienta que el modelo puede pedir. El esquema describe su entrada.</summary>
public sealed record HerramientaDelModelo(string Nombre, string Descripcion, string JsonSchema);

public sealed record RespuestaDeConversacion(IReadOnlyList<BloqueDeTurno> Bloques, string? MotivoDeParada, UsoDeIA Uso, string Modelo)
{
    public IEnumerable<BloqueDeUsoDeHerramienta> UsosDeHerramientas => Bloques.OfType<BloqueDeUsoDeHerramienta>();

    public string Texto => string.Join("\n\n", Bloques.OfType<BloqueDeTexto>().Select(b => b.Texto)).Trim();
}
