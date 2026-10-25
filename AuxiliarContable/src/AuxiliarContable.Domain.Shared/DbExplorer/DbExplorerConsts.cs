namespace AuxiliarContable.DbExplorer;

/// <summary>
/// Constantes compartidas del explorador de BD. Viven en Domain.Shared porque las
/// necesitan tanto los DTOs (Application.Contracts) como el application service.
/// </summary>
public static class DbExplorerConsts
{
    /// <summary>
    /// Esquema por defecto del motor. En PostgreSQL es <c>public</c>; es el equivalente
    /// de <c>dbo</c> en SQL Server.
    /// </summary>
    public const string DefaultSchema = "public";
}
