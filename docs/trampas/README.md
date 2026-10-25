# Índice de trampas detalladas

Este directorio es la bitácora detallada de "lo que no se ve leyendo el código": cada
entrada explica una trampa real, por qué existe, cómo se encontró y qué la vigila. Se separó
de `CLAUDE.md` porque ese archivo se carga entero en cada sesión — con el historial completo
superaba los 150k caracteres. Aquí cada archivo se lee **solo cuando el tema es relevante**.

| Archivo | Contiene |
|---|---|
| [nucleo-abp.md](nucleo-abp.md) | Transaccionalidad, filtro de tenant/soft delete en EF y SQL crudo, auditoría, DI por convención, la trampa de `UseErrorPage()`, Angular fuera de la zona (`NgZone`), 2FA por Telegram |
| [jobs-avisos-backups.md](jobs-avisos-backups.md) | Background jobs (`/jobs`), avisos dentro de la app (la campana), caducidad de claves de API, copias de seguridad vigiladas |
| [archivos-api-keys-seguridad.md](archivos-api-keys-seguridad.md) | BLOBs/foto de perfil, claves de API (`/api-keys`), bloqueo de IPs manual y automático, registro de seguridad, historial de cambios, límites por identidad, export CSV, sesiones activas |
| [frontend-ui-general.md](frontend-ui-general.md) | Menú y tablas apilables en móvil, sistema de traducción (es/en) |
| [multitenancy-y-marca.md](multitenancy-y-marca.md) | Cómo funciona de verdad el multi-tenancy, espacio por inquilino (`/app/{nombre}`), alta y suplantación de administrador, baja de inquilinos, marca/branding por inquilino |
| [modulos-features-y-tipos-usuario.md](modulos-features-y-tipos-usuario.md) | Features vs. permisos como ejes distintos, cuotas por inquilino, purga de baja, importación CSV, tipos de usuario como plantillas, trampas de tests de integración, coste de despliegue |
| [piel-accesibilidad-y-componentes.md](piel-accesibilidad-y-componentes.md) | Tipografía y tokens del tema, accesibilidad (contraste WCAG), tests de front con Vitest, componentes compartidos, paleta de comandos (Ctrl+K), techo de las listas, "Mi cuenta" |
| [acceso-superadmin.md](acceso-superadmin.md) | Esconder la puerta del superadministrador (segmento oculto), el auto-registro, pantalla en blanco antes de arrancar Angular |

Al buscar una trampa concreta, es más rápido `grep` sobre estos archivos que releer todo el
directorio.
