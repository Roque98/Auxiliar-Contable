namespace AuxiliarContable.Credenciales;

/// <summary>Qué credencial del cliente: la e.firma (.cer, .key y su contraseña) o la contraseña del portal (CIEC).</summary>
public enum TipoDeCredencial
{
    Efirma = 0,
    Ciec = 1,
}

/// <summary>Lo que se hizo con una credencial: queda en la bitácora con quién y cuándo.</summary>
public enum AccionSobreCredencial
{
    Guardar = 0,
    VerContrasena = 1,
    DescargarCer = 2,
    DescargarKey = 3,
    Borrar = 4,
}

public static class CredencialesConsts
{
    /// <summary>Un .cer o .key del SAT pesa 1-2 KB; esto deja margen y corta archivos que no son eso.</summary>
    public const int MaxBytesDeArchivo = 16 * 1024;

    public const int MaxContrasenaLength = 128;
    public const int RfcLength = 13;
    public const int NumeroDeCertificadoLength = 40;

    /// <summary>Intentos de NIP o contraseña fallidos seguidos antes de bloquear.</summary>
    public const int MaxIntentos = 5;

    public const int MinutosDeBloqueo = 15;

    /// <summary>La e.firma avisa que vence cuando faltan estos días o menos.</summary>
    public const int DiasDeAviso = 30;
}
