namespace AuxiliarContable.DataSources;

/// <summary>
/// Nombres lógicos de las fuentes de datos del proyecto.
/// Todo el código referencia estas constantes, nunca el string literal.
/// La cadena de conexión real vive en <c>ConnectionStrings:{Nombre}</c>
/// (resuelta por ABP, respetando multi-tenancy) y los metadatos en la
/// sección <c>DataSources:{Nombre}</c> de configuración.
/// </summary>
public static class DataSourceNames
{
    /// <summary>Réplica de reportes de solo lectura (ejemplo de fuente externa vía Dapper).</summary>
    // Una constante por cada fuente de datos externa de solo lectura (ver la skill
    // `datasource`). Hoy no hay ninguna: la de ejemplo («Reportes») se fue con el módulo de
    // productos.
}
