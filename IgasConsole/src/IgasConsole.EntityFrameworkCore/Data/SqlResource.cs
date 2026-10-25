using System;
using System.IO;
using System.Reflection;

namespace IgasConsole.Data;

/// <summary>
/// Carga archivos <c>.sql</c> embebidos como recursos, para que las consultas
/// vivan como archivos de texto versionados (visibles en <c>git diff</c>) y no
/// como strings dispersos en clases C#.
/// <para>Uso: <c>SqlResource.Load("Products/Sql/GetProductSalesReport.sql")</c>.</para>
/// El nombre del recurso se deriva del RootNamespace del ensamblado
/// (<c>IgasConsole</c>) más la ruta relativa con puntos.
/// </summary>
public static class SqlResource
{
    private static readonly Assembly ResourceAssembly = typeof(SqlResource).Assembly;
    private const string RootNamespace = "IgasConsole";

    public static string Load(string relativePath)
    {
        var resourceName = RootNamespace + "." +
            relativePath.Replace('/', '.').Replace('\\', '.');

        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"No se encontró el recurso SQL embebido '{resourceName}'. " +
                "¿Está el .sql marcado como <EmbeddedResource> en el .csproj?");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
