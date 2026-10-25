---
description: Arranca la creación de una feature ABP nueva copiando la feature de referencia Product.
argument-hint: [nombre-de-la-feature]
---

> ⚠️ En Igas el módulo `Product` ya no existe: la feature de referencia es `Organizaciones` (ver CLAUDE.md §10). Para el patrón de SQL crudo/Dapper, el código de `Product` sigue en abt-template (`D:/proyectos/perso/abt-template`).

Crea una feature ABP nueva siguiendo la skill `abp-feature`, copiando la estructura
de la feature de referencia `Product`. Feature solicitada: $ARGUMENTS

Verifica cada API de ABP en `refs/abp` (Regla Cero) y deja `dotnet build` y los tests en verde.
