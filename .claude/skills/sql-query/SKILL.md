---
name: sql-query
description: Añade una consulta de LECTURA con SQL crudo — SqlQueryRaw para la base principal o Dapper para una fuente externa. Úsala para reportes, listados complejos o agregaciones.
---

> ⚠️ En esta consola el módulo `Product` ya no existe: la feature de referencia es `Organizaciones` (ver CLAUDE.md §10). Para el patrón de SQL crudo/Dapper, el código de `Product` sigue en abt-template (`D:/proyectos/perso/abt-template`).


# Agregar una query de lectura explícita

## ¿Cuál usar?

- **Base principal → `SqlQueryRaw<TDto>`** sobre el `DbContext` del Unit of Work
  (participa de su transacción). Va en un repositorio custom en la capa
  EntityFrameworkCore. Referencia: `EntityFrameworkCore/Products/ProductRepository.cs`.
- **Fuente externa de solo lectura → Dapper** vía `IExternalDataSourceConnectionFactory`.
  Referencia: `EntityFrameworkCore/Data/ExternalSalesReportReader.cs`.

## Pasos

1. Crea el `.sql` embebido por feature:
   - Principal: `EntityFrameworkCore/<Feature>/Sql/<Nombre>.sql`
   - Externa: `EntityFrameworkCore/Data/Sql/<Nombre>.sql`
   (El `.csproj` ya embebe `**/*.sql`.)
2. Define el read model (Domain) y/o el DTO (Contracts) con propiedades que
   coincidan por nombre con las columnas del `SELECT`.
3. Carga el SQL con `SqlResource.Load("<ruta relativa>.sql")`.
4. Ejecuta:
   - `SqlQueryRaw`: parámetros **por posición** `{0}, {1}` → `dbContext.Database.SqlQueryRaw<T>(sql, p0, p1).ToListAsync(ct)`.
   - Dapper: parámetros **nombrados** `@Nombre` → `connection.QueryAsync<T>(new CommandDefinition(sql, new { Nombre = valor }, cancellationToken: ct))`.

## ⚠️ Advertencia obligatoria (soft delete y tenant)

El SQL crudo **NO** aplica los filtros globales de ABP (ni `SqlQueryRaw` ni Dapper).
En cada `.sql` de la **base principal** agrega A MANO:
- `WHERE IsDeleted = CAST(0 AS bit)`
- filtro de tenant: `((TenantId IS NULL AND {n} IS NULL) OR TenantId = {n})` pasando `CurrentTenant.Id`.

Ver el ejemplo comentado en `Products/Sql/GetProductSalesReport.sql`.

## Prohibido
- Concatenar variables en el string del SQL. Siempre parámetros.
- `dynamic` fuera del método: cada query devuelve un DTO/tipo explícito.
- Escrituras con SQL crudo en la base principal (usa EF Core/LINQ).

## Test
Agrega un test de integración (PostgreSQL) que verifique el resultado y, si la entidad
tiene soft delete, que un registro borrado NO aparezca. Referencia:
`Integration.Tests/Products/ProductAppServiceIntegrationTests.cs`.
