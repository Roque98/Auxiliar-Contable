-- Tamaño de la base y conexiones abiertas.
--
-- Se consulta a la propia base porque nadie más lo sabe: ni /proc ni el proceso de la API
-- tienen esa información. Es barato (pg_stat_activity es una vista en memoria) y una vez
-- por minuto no pesa nada.
SELECT (pg_database_size(current_database()) / 1024 / 1024)::int AS "DbSizeMb",
       -- Todas las conexiones a ESTA base, no las del servidor entero: lo que importa es
       -- cuánto se está acercando la aplicación a su propio límite de pool.
       (SELECT count(*) FROM pg_stat_activity
        WHERE datname = current_database())::int              AS "DbConnections"
