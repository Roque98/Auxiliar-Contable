---
description: Audita todos los .sql del repo buscando concatenación, parámetros mal usados y queries sin test.
---

Audita todos los archivos `.sql` bajo `AuxiliarContable/src`:

1. **Concatenación**: reporta cualquier `.sql` (o construcción de SQL en C#) que
   arme el string con variables en vez de parámetros.
2. **Parámetros**: en queries de la base principal deben ir por **posición**
   (`{0}`, `{1}`); en fuentes externas (Dapper), **nombrados** (`@Nombre`).
   Marca los que estén al revés o sin parametrizar.
3. **Soft delete / tenant**: en `.sql` de la base principal que tocan entidades
   con soft delete, verifica que exista `WHERE IsDeleted = 0` y el filtro de `TenantId`.
4. **Cobertura**: marca las queries sin un test de integración que las ejercite.

Reporta hallazgos por archivo con la línea y la corrección sugerida. No modifiques
nada sin confirmación salvo que se te pida.
