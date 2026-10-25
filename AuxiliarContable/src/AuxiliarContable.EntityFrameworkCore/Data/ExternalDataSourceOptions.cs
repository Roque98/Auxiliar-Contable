using System;
using System.Collections.Generic;

namespace AuxiliarContable.Data;

/// <summary>
/// Proveedores soportados por <see cref="IExternalDataSourceConnectionFactory"/>.
/// El valor viaja en <c>DataSources:{Nombre}:Provider</c> y decide qué driver
/// ADO.NET se instancia. Añadir uno nuevo = añadir su driver al .csproj y un
/// caso al switch de la factory.
/// </summary>
public static class ExternalDataSourceProviders
{
    public const string PostgreSql = "PostgreSql";
    public const string SqlServer = "SqlServer";
}

/// <summary>
/// Metadatos (sin credenciales) de las fuentes de datos externas, enlazados
/// desde la sección <c>DataSources</c> de configuración. Las credenciales/cadenas
/// viven en <c>ConnectionStrings</c> (user-secrets / variables de entorno).
/// </summary>
public class ExternalDataSourceOptions
{
    public Dictionary<string, ExternalDataSourceDescriptor> Sources { get; set; } = new();
}

public class ExternalDataSourceDescriptor
{
    /// <summary>
    /// Proveedor de la fuente: uno de <see cref="ExternalDataSourceProviders"/>.
    /// NO es informativo: determina el driver que abre la conexión, así que una
    /// fuente externa puede correr en un motor distinto al de la base principal.
    /// </summary>
    public string Provider { get; set; } = ExternalDataSourceProviders.PostgreSql;

    /// <summary>Debe ser true: estas fuentes son de solo lectura por diseño.</summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>Timeout por defecto para los comandos contra esta fuente.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;
}
