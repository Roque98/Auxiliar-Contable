---
name: datasource
description: Registra una fuente de datos externa de solo lectura nueva (constante, configuración, secreto y test de conectividad) para consultarla con Dapper.
---

# Registrar una fuente de datos externa nueva

1. **Constante** en `src/AuxiliarContable.Domain.Shared/DataSources/DataSourceNames.cs`:
   ```csharp
   public const string <Nombre> = "<Nombre>";
   ```
2. **Metadatos** (sin credenciales) en `HttpApi.Host/appsettings.json`, sección `DataSources`:
   ```json
   "DataSources": { "<Nombre>": { "Provider": "PostgreSql", "ReadOnly": true, "DefaultTimeoutSeconds": 30 } }
   ```
3. **Cadena de conexión** en `ConnectionStrings:<Nombre>`:
   - Dev: `dotnet user-secrets set "ConnectionStrings:<Nombre>" "<cadena>"` (desde `HttpApi.Host`).
   - Prod: variable de entorno / `.env` no versionado. **Nunca** en appsettings.
4. **Consumir**: inyecta `IExternalDataSourceConnectionFactory` y llama
   `CreateOpenConnectionAsync(DataSourceNames.<Nombre>)`; usa Dapper (ver skill `sql-query`).
   La resolución de la cadena la hace ABP (`IConnectionStringResolver`), respetando multi-tenancy.
5. **Test de conectividad/lectura** en `Integration.Tests` (crea/siembra la base externa
   en la fixture, como `PostgresFixture.CreateAndSeedExternalDatabaseAsync`).

## Reglas
- Estas fuentes son SOLO LECTURA. No escribas en ellas.
- La conexión se abre y cierra por operación, **fuera** del Unit of Work de la base principal.
- Solo el factory abre conexiones (lo verifica un test de arquitectura).
