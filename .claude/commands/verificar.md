---
description: Ejecuta la verificación completa del repo (build, tests, dependencias vulnerables, gitleaks) y reporta.
---

Ejecuta la skill `verify` de principio a fin (build, tests unitarios/arquitectura/
integración, `dotnet list package --vulnerable`, `gitleaks`) y reporta el resultado
de cada paso con ✅/❌. Corrige lo que falle antes de terminar.
