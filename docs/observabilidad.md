# Observabilidad

Una herramienta (**OpenObserve**, autoalojado) y un estándar (**OpenTelemetry**). Los datos no salen
del VPS. Lo que ya había (métricas por minuto en `/metrics` y `/system-status`, avisos a la campana y
Telegram, auditoría) sigue igual: es el resumen para el equipo. Esto es para **diagnosticar**.

| Qué | Cómo llega | Dónde se ve |
|---|---|---|
| Trazas de la API: cada petición con sus consultas a PostgreSQL, llamadas HTTP y órdenes a MeshCentral | OpenTelemetry (OTLP/HTTP) | OpenObserve → Traces |
| Métricas de la API: por ruta (p50/p95, errores) y del runtime (GC, hilos) | OpenTelemetry | OpenObserve → Metrics |
| Logs de la API (los de Serilog, con el id de la traza) | `Serilog.Sinks.OpenTelemetry` | OpenObserve → Logs (y el archivo de siempre) |
| Errores de JavaScript de la web y de la app | A la API (`/api/app/errores-del-cliente/registrar`), que los registra como log | OpenObserve → Logs, `Error en el cliente` |
| Que los sitios respondan, visto desde FUERA del VPS | Workflow `vigilancia.yml` del repo público Roque98/ZIA, cada 10 min | Telegram, solo cuando algo cambia |

## OpenObserve

- `/opt/openobserve` (copia versionada en `deploy/openobserve/`): imagen fijada (`v1.1.2`), solo en
  `127.0.0.1:5080`, datos en `/opt/openobserve/data`, **30 días** de retención, telemetría al fabricante
  apagada, contenedor con tope de 1 GB (en reposo usa ~200 MB).
- Usuario administrador y contraseña: `/etc/openobserve/env` (600). La consola usa la credencial de
  **ingesta** (el passcode, no la contraseña), en `/etc/zia-console/env`:
  `Observabilidad__Otlp__Endpoint=http://127.0.0.1:5080/api/default` y `Observabilidad__Otlp__Authorization=Basic …`.
- Acceso: `https://obs.ziacentro.mx` (nginx: `deploy/openobserve/nginx-obs.conf`).
- Actualizar: cambiar la etiqueta en el compose, `docker compose pull && docker compose up -d`.

## En el código

- `HttpApi.Host/Observabilidad/`: `AddObservabilidad` (trazas y métricas) y `ObservabilidadOptions`.
  Sin `Observabilidad:Otlp:Endpoint` no se exporta nada (desarrollo y pruebas).
- Logs: `Program.cs` añade el sink de OpenTelemetry a Serilog; el archivo sigue de respaldo.
- MeshCentral va por WebSocket: sus órdenes se trazan a mano (`ClienteMeshCentral.Trazas`).
- No se trazan `/health-status`, `/health-ui` ni estáticos (ruido): las métricas sí los cuentan.
- ⚠️ Los logs del propio exportador (`System.Net.Http.HttpClient.Otlp*Exporter`) y del health check que
  se consulta a sí mismo (`…health-checks`) van a `Warning` en `appsettings.json`: con `Information`
  eran la mitad de los logs, y los del exportador se reenviaban a sí mismos en cada lote.
- Errores del cliente: `ErroresDelClienteAppService` (con sesión, fuera de la auditoría, 20/min por
  persona). Web: `shared/reporte-de-errores.ts` desde el `ErrorHandler` (`RecargaTrasDespliegue`); no
  manda los fallos de carga tras un despliegue ni la falta de red. App: `src/util/reporte-de-errores.ts`
  (Roque98/ZIA-App).

## Qué mirar cuando algo falla

1. ¿Llegó un aviso de la vigilancia o de la campana? Dice qué y desde cuándo.
2. OpenObserve → Logs, filtrando `severity = ERROR` o `Error en el cliente` en la última hora.
3. Del log, el `trace_id` → Traces: la petición completa, con qué consulta o llamada tardó o falló.
