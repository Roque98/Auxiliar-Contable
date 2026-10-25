-- Serie temporal agregada en cubos de N minutos.
--
-- Identificadores entre comillas dobles: EF Core crea las tablas en PascalCase y sin
-- comillas PostgreSQL las plegaría a minúsculas. Los alias también, para que el
-- materializador de EF encuentre las propiedades de MetricSeriesPoint.
--
-- Nota sobre los filtros globales de ABP: aquí NO hacen falta los "IsDeleted" = false
-- ni el filtro de TenantId que sí llevan los .sql de la base principal. ServerMetric no
-- implementa ISoftDelete ni IMultiTenant a propósito (ver la entidad).
--
-- Parámetros por posición: {0} = desde (UTC), {1} = hasta (UTC), {2} = minutos por cubo.
WITH muestras AS (
    SELECT m.*,
           -- El uptime del registro anterior. Un uptime que BAJA solo puede significar
           -- una cosa: el proceso se reinició entre las dos muestras. Es la señal más
           -- fácil de perder mirando promedios, y la que explica la mitad de los
           -- comportamientos raros de un servidor.
           LAG(m."UptimeSeconds") OVER (ORDER BY m."Timestamp") AS uptime_anterior
    FROM   "AppServerMetrics" m
    -- El rango se filtra ANTES del LAG para no recorrer la tabla entera, pero se toma un
    -- margen hacia atrás para que la primera muestra del rango tenga con qué comparar.
    WHERE  m."Timestamp" >= {0} - interval '10 minutes'
      AND  m."Timestamp" <  {1}
)
SELECT date_bin(make_interval(mins => {2}), s."Timestamp", TIMESTAMPTZ '2001-01-01') AS "Bucket",
       AVG(s."CpuPercent")                                     AS "CpuPercent",
       AVG(s."MemoryUsedMb"::double precision
           / NULLIF(s."MemoryTotalMb", 0) * 100)               AS "MemoryPercent",
       AVG(s."SwapUsedMb"::double precision
           / NULLIF(s."SwapTotalMb", 0) * 100)                 AS "SwapPercent",
       AVG(s."DiskUsedPercent")                                AS "DiskUsedPercent",
       -- MAX y no AVG: lo que importa de una base que crece es hasta dónde llegó.
       MAX(s."DbSizeMb")::double precision                     AS "DbSizeMb",
       -- MAX también: el pico de conexiones es lo que agota el pool, no la media.
       MAX(s."DbConnections")::double precision                AS "DbConnections",
       SUM(s."RequestCount")                                   AS "RequestCount",
       SUM(s."ErrorCount")                                     AS "ErrorCount",
       SUM(s."ClientErrorCount")                               AS "ClientErrorCount",
       SUM(s."RateLimitedCount")                                AS "RateLimitedCount",
       -- Media PONDERADA por número de peticiones: la media de las medias mentiría
       -- cuando un minuto tuvo 2 peticiones y el siguiente 2.000.
       SUM(s."AvgResponseMs" * s."RequestCount")
           / NULLIF(SUM(s."RequestCount"), 0)                  AS "AvgResponseMs",
       MAX(s."MaxResponseMs")                                  AS "MaxResponseMs",
       COUNT(*) FILTER (WHERE s."HealthStatus" <> 'Healthy')    AS "UnhealthySamples",
       COUNT(*) FILTER (WHERE s.uptime_anterior IS NOT NULL
                          AND s."UptimeSeconds" < s.uptime_anterior) AS "Restarts"
FROM   muestras s
-- Ahora sí, el rango exacto: el margen de arriba solo servía para calcular el LAG.
WHERE  s."Timestamp" >= {0}
GROUP BY 1
ORDER BY 1
