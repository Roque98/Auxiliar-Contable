-- Espacio que ocupan los archivos de un inquilino, en bytes.
--
-- Parametro {0}: el TenantId, o NULL para el host.
--
-- ⚠️ Todo identificador va entre comillas dobles: sin ellas PostgreSQL pliega a minúsculas
-- y "AbpBlobs" no existiría (§4). Y el alias "Value" es obligatorio — un escalar leído con
-- SqlQueryRaw<long> exige que la columna se llame así.
--
-- ⚠️ Sin `;` final. SqlQueryRaw devuelve un IQueryable y el `SingleAsync` de quien llama le
-- añade un LIMIT envolviendo esto en una subconsulta; el punto y coma de en medio daría
-- «42601: syntax error at or near ";"».
--
-- ⚠️ El filtro de inquilino se escribe A MANO. El SQL crudo no pasa por los filtros globales
-- de ABP aunque corra sobre el DbContext del Unit of Work: sin esta línea, la cuota de cada
-- inquilino se mediría contra los archivos de TODA la instalación y el primero en subir algo
-- dejaría a los demás sin sitio.
--
-- `IS NOT DISTINCT FROM` y no `=`: el host tiene TenantId NULL, y `NULL = NULL` es NULL en
-- SQL, así que con `=` el host mediría siempre 0. El CAST es necesario porque Npgsql no
-- puede inferir el tipo de un parámetro que viaja NULL.
--
-- COALESCE porque SUM sobre cero filas devuelve NULL, no 0: sin él, un inquilino que aún no
-- ha subido nada haría fallar la conversión a long.
SELECT COALESCE(SUM(LENGTH(b."Content")), 0) AS "Value"
FROM "AbpBlobs" AS b
WHERE b."TenantId" IS NOT DISTINCT FROM CAST({0} AS uuid)
