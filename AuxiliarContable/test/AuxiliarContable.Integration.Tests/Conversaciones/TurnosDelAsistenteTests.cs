using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.IA;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Al cambiar de tema, el asistente retomaba la investigación de la pregunta anterior: veía todas sus búsquedas y
/// su pendiente. De los turnos anteriores solo debe ver lo que se dijo; el turno en curso va completo.
/// </summary>
public class TurnosDelAsistenteTests
{
    private static readonly Guid Conversacion = Guid.NewGuid();
    private int _numero;
    private readonly List<MensajeDeConversacion> _historial = [];

    private void Agregar(RolDelMensaje rol, string visible, params BloqueDeTurno[] bloques) =>
        _historial.Add(new MensajeDeConversacion(Guid.NewGuid(), Conversacion, ++_numero, rol, visible, bloques, DateTime.UtcNow));

    private static BloqueDeUsoDeHerramienta Uso(string id) =>
        new(id, "consultar_leyes", JsonDocument.Parse("""{"pregunta":"subsidio para el empleo"}""").RootElement);

    [Fact]
    public void De_los_turnos_anteriores_solo_va_la_pregunta_y_la_respuesta_visible()
    {
        Agregar(RolDelMensaje.Usuario, "¿Aplica el subsidio?", new BloqueDeTexto("¿Aplica el subsidio?"));
        Agregar(RolDelMensaje.Asistente, string.Empty, Uso("u1"));
        Agregar(RolDelMensaje.Herramientas, string.Empty, new BloqueDeResultadoDeHerramienta("u1", new string('x', 40000)));
        Agregar(RolDelMensaje.Asistente, "Sí aplica; la tarifa semanal no la pude obtener.", new BloqueDeTexto("Sí aplica; la tarifa semanal no la pude obtener."));
        Agregar(RolDelMensaje.Usuario, "¿Qué es el régimen general de personas morales?", new BloqueDeTexto("¿Qué es el régimen general de personas morales?"));
        Agregar(RolDelMensaje.Asistente, string.Empty, Uso("u2"));
        Agregar(RolDelMensaje.Herramientas, string.Empty, new BloqueDeResultadoDeHerramienta("u2", "art. 9 LISR"));

        var turnos = AsistenteAppService.Turnos(_historial);

        turnos.Count.ShouldBe(5);
        turnos.Take(2).Select(t => t.Rol).ShouldBe([RolDelTurno.Usuario, RolDelTurno.Asistente]);
        turnos.Take(2).SelectMany(t => t.Bloques).ShouldAllBe(b => b is BloqueDeTexto);
        ((BloqueDeTexto)turnos[1].Bloques[0]).Texto.ShouldStartWith("Sí aplica");
        turnos.Skip(2).SelectMany(t => t.Bloques).OfType<BloqueDeResultadoDeHerramienta>().Single().Contenido.ShouldBe("art. 9 LISR");
    }

    [Fact]
    public void Recorta_por_pares_completos()
    {
        for (var i = 0; i < ConversacionConsts.MensajesDeHistorial; i++)
        {
            Agregar(RolDelMensaje.Usuario, $"p{i}", new BloqueDeTexto($"p{i}"));
            Agregar(RolDelMensaje.Asistente, $"r{i}", new BloqueDeTexto($"r{i}"));
        }

        Agregar(RolDelMensaje.Usuario, "última", new BloqueDeTexto("última"));

        var turnos = AsistenteAppService.Turnos(_historial);

        turnos[0].Rol.ShouldBe(RolDelTurno.Usuario);
        turnos.Count.ShouldBe(ConversacionConsts.MensajesDeHistorial + 1);
        ((BloqueDeTexto)turnos[^1].Bloques[0]).Texto.ShouldBe("última");
    }
}
