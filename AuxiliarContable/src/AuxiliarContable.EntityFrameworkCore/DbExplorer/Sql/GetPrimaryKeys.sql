-- Columnas que forman la clave primaria de una tabla (parametros por valor).
-- information_schema es estandar: esta consulta es la misma en SQL Server y PostgreSQL.
SELECT kcu.column_name AS "ColumnName"
FROM   information_schema.table_constraints tc
JOIN   information_schema.key_column_usage kcu
       ON tc.constraint_name = kcu.constraint_name
      AND tc.table_schema    = kcu.table_schema
WHERE  tc.constraint_type = 'PRIMARY KEY'
  AND  tc.table_schema    = @TableSchema
  AND  tc.table_name      = @TableName;
