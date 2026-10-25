namespace IgasConsole.Organizaciones;

public static class OrganizacionConsts
{
    public const int MaxNombreLength = 128;
    public const int MaxDescripcionLength = 512;
    public const int MaxRfcLength = 13;

    /// <summary>El RFC tal como se compara (mayúsculas, sin espacios). Vacío = null.</summary>
    public static string? NormalizarRfc(string? rfc) =>
        string.IsNullOrWhiteSpace(rfc) ? null : rfc.Trim().ToUpperInvariant();
}
