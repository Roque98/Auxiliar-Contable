namespace AuxiliarContable.Conversaciones;

public static class SolicitudConsts
{
    public const int MaxTextoLength = 1000;
    public const int MaxRespuestaLength = 1000;

    /// <summary>Lo más que se le pasa al modelo de una lista de solicitudes.</summary>
    public const int MaxEnLista = 20;
}

/// <summary>Quién le pide a quién.</summary>
public enum SentidoDeSolicitud
{
    /// <summary>El cliente al despacho: un cambio, una corrección o una aclaración que él no puede hacer.</summary>
    AlDespacho = 0,

    /// <summary>El despacho al cliente: un documento, un dato o una aclaración.</summary>
    AlCliente = 1,
}

public enum TipoDeSolicitud
{
    /// <summary>Cambiar o corregir algo (una operación, una póliza, un dato confirmado).</summary>
    Cambio = 0,

    Documento = 1,
    Aclaracion = 2,
}

public enum EstadoDeSolicitud
{
    Pendiente = 0,
    Atendida = 1,
    Rechazada = 2,
}
