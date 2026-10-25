namespace AuxiliarContable.Conversaciones;

public static class ConversacionConsts
{
    /// <summary>Lo que puede escribir una persona en un mensaje. Una plática, no un documento.</summary>
    public const int MaxTextoDelUsuario = 4000;

    /// <summary>Texto visible de un mensaje (lo del asistente incluido).</summary>
    public const int MaxTextoLength = 16000;

    /// <summary>
    /// Mensajes que se le repiten al modelo en cada turno. Más historial = más tokens en CADA
    /// llamada; lo que importa del cliente vive en el perfil, no en la conversación.
    /// </summary>
    public const int MensajesDeHistorial = 30;

    /// <summary>Vueltas de herramientas por mensaje. Corta un bucle del modelo antes de que cueste.</summary>
    public const int MaxVueltasDeHerramientas = 6;

    /// <summary>Mensajes por persona en 24 h: control de costo (CLAUDE.md §1.2.8).</summary>
    public const int MaxMensajesPorDia = 200;
}

public enum RolDelMensaje
{
    Usuario = 1,
    Asistente = 2,

    /// <summary>Resultados de herramientas: el modelo los necesita, la persona no los ve.</summary>
    Herramientas = 3,
}
