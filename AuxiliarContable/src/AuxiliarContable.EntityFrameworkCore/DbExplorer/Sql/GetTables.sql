-- Lista las tablas base del esquema (introspeccion). Sin parametros.
-- A diferencia de SQL Server, en PostgreSQL information_schema.tables incluye los
-- catalogos del sistema: hay que excluir pg_catalog e information_schema.
SELECT t.table_schema AS "TableSchema",
       t.table_name   AS "TableName"
FROM   information_schema.tables t
WHERE  t.table_type = 'BASE TABLE'
  AND  t.table_schema NOT IN ('pg_catalog', 'information_schema')
ORDER BY t.table_schema, t.table_name;
