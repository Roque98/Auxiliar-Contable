-- Columnas de una tabla. Los nombres van como VALORES parametrizados (@TableSchema,
-- @TableName), nunca interpolados. Devuelve tipo, nullabilidad, longitud y si es
-- identity/generada (solo lectura).
--
-- PostgreSQL expone esto en el information_schema estandar (is_identity / is_generated),
-- sin necesidad de bajar a pg_catalog como haria COLUMNPROPERTY en SQL Server. El
-- unico caso que el estandar no cubre es el de las columnas serial heredadas, que no
-- son IDENTITY sino un DEFAULT nextval(...): se detectan mirando column_default.
SELECT c.column_name              AS "ColumnName",
       c.data_type                AS "DataType",
       CASE WHEN c.is_nullable = 'YES' THEN 1 ELSE 0 END AS "IsNullable",
       c.character_maximum_length AS "MaxLength",
       CASE WHEN c.is_identity = 'YES'
              OR c.column_default LIKE 'nextval(%'
            THEN 1 ELSE 0 END     AS "IsIdentity",
       CASE WHEN c.is_generated = 'ALWAYS' THEN 1 ELSE 0 END AS "IsComputed"
FROM   information_schema.columns c
WHERE  c.table_schema = @TableSchema
  AND  c.table_name   = @TableName
ORDER BY c.ordinal_position;
