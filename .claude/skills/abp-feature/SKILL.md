---
name: abp-feature
description: Crea un slice/feature completo de ABP (entidad, DTOs, application service, repositorio, migración y tests) copiando la feature de referencia Product. Úsala al añadir una entidad o caso de uso nuevo al backend.
---

> ⚠️ En Igas el módulo `Product` ya no existe: la feature de referencia es `Organizaciones` (ver CLAUDE.md §10). Para el patrón de SQL crudo/Dapper, el código de `Product` sigue en abt-template (`D:/proyectos/perso/abt-template`).

# Crear una feature ABP nueva

Copia EXACTAMENTE la estructura de la feature de referencia `Product`. Verifica cada
API de ABP en `refs/abp` antes de usarla (Regla Cero) y corre `dotnet build` al terminar.

## Pasos

1. **Entidad** en `src/IgasConsole.Domain/<Feature>/<Entity>.cs`
   - Hereda de `FullAuditedAggregateRoot<Guid>` e implementa `IMultiTenant` si aplica.
   - Constructor con `Check.NotNullOrWhiteSpace(...)`. Referencia: `Domain/Products/Product.cs`.
2. **Constantes** en `src/IgasConsole.Domain.Shared/<Feature>/<Feature>Consts.cs` (longitudes, etc.).
3. **DbSet + mapeo** en `src/IgasConsole.EntityFrameworkCore/EntityFrameworkCore/IgasConsoleDbContext.cs`
   - `b.ToTable(IgasConsoleConsts.DbTablePrefix + "<Plural>", IgasConsoleConsts.DbSchema); b.ConfigureByConvention();`
4. **DTOs** en `src/IgasConsole.Application.Contracts/<Feature>/` (input y output; usa DataAnnotations).
5. **Interfaz de app service** en `.../Application.Contracts/<Feature>/I<Feature>AppService.cs` (`: IApplicationService`).
6. **Application service** en `src/IgasConsole.Application/<Feature>/<Feature>AppService.cs`
   - Hereda de `IgasConsoleAppService`. Escritura con `IRepository<Entity,Guid>` (`InsertAsync(..., autoSave:true)`).
   - Auditoría y `TenantId` se llenan solos; usa `CurrentTenant.Id` al construir la entidad.
7. **Lectura con SQL crudo** (si necesitas reportes/listados): usa la skill `sql-query`.
8. **Migración**: `cd IgasConsole/src/IgasConsole.EntityFrameworkCore && dotnet ef migrations add Added_<Feature>` y luego `dotnet run --project ../IgasConsole.DbMigrator`.
9. **Tests de integración** en `test/IgasConsole.Integration.Tests/<Feature>/`, heredando de `IntegrationTestBase`. Referencia: `Products/ProductAppServiceIntegrationTests.cs`.

## Reglas
- No expongas entidades de dominio: devuelve DTOs (lo verifica un test de arquitectura).
- `dotnet build` y `dotnet test` deben quedar en verde antes de terminar (ver skill `verify`).
