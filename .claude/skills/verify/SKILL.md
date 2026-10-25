---
name: verify
description: Secuencia completa de verificación antes de dar por terminado cualquier trabajo en este repo (build, tests, dependencias vulnerables, secretos, TODOs). Ejecútala antes de decir que algo está listo.
---

# Verificación completa (antes de terminar)

Ejecuta desde `IgasConsole/` y reporta el resultado de cada paso. Si algo falla, corrígelo antes de terminar.

1. **Build** (warnings = errores):
   ```bash
   dotnet build IgasConsole.slnx -c Debug
   ```
2. **Tests unitarios / EF (SQLite)**:
   ```bash
   dotnet test test/IgasConsole.EntityFrameworkCore.Tests/*.csproj
   ```
3. **Tests de arquitectura**:
   ```bash
   dotnet test test/IgasConsole.Architecture.Tests/*.csproj
   ```
4. **Tests de integración (PostgreSQL + Respawn)**:
   ```bash
   dotnet test test/IgasConsole.Integration.Tests/*.csproj
   ```
5. **Dependencias vulnerables**:
   ```bash
   dotnet list package --vulnerable --include-transitive
   ```
6. **Secretos** (binario nativo, sin Docker):
   ```bash
   gitleaks detect --source . --no-banner
   ```
7. **Revisión manual**: sin `TODO`/`FIXME` nuevos sin resolver, sin warnings suprimidos
   con `#pragma`/`NoWarn` añadidos para tapar problemas, y sin secretos en `appsettings*.json`.

**Reporta**: un resumen con ✅/❌ por paso y qué corregiste.
