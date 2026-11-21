import { authGuard, permissionGuard } from '@abp/ng.core';
import { Routes } from '@angular/router';
import { environment } from '../environments/environment';
export const APP_ROUTES: Routes = [
  {
    // La PORTADA: con el chat encendido manda a /asistente (clientes y despacho entran platicando);
    // apagado, cifras del mes y atajos a las pantallas. Sin guardia A PROPÓSITO: sin sesión el
    // layout pinta su portada de acceso y no monta la ruta; un authGuard aquí mandaría directo al
    // login y anunciaría el acceso oculto (§6.98).
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./inicio/inicio.component').then(c => c.InicioComponent),
  },
  {
    // El chat con IA. Si está apagado en el servidor, la pantalla lo dice y el servicio lo rechaza.
    // El servidor decide qué clientes ve cada quien (Autorizador).
    path: 'asistente',
    loadComponent: () => import('./asistente/asistente.component').then(c => c.AsistenteComponent),
    canActivate: [authGuard],
  },
  {
    // Sin permiso: la ley es pública; solo hace falta sesión.
    path: 'leyes',
    loadComponent: () => import('./leyes/leyes.component').then(c => c.LeyesComponent),
    canActivate: [authGuard],
  },
  {
    // La hoja concentrado: lo primero que ve el cliente (deudas fiscales hasta la factura). La ven el cliente y el
    // despacho; el servidor decide qué clientes ve cada quien (Autorizador).
    path: 'concentrado',
    loadComponent: () => import('./concentrado/concentrado.component').then(c => c.ConcentradoComponent),
    canActivate: [authGuard],
  },
  {
    // Mi información: facturas, dinero, control interno y todo (cada operación con sus tres patas). El servidor
    // decide qué clientes ve cada quien y quién sube (Autorizador).
    path: 'mi-informacion',
    loadComponent: () => import('./mi-informacion/mi-informacion.component').then(c => c.MiInformacionComponent),
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'facturas' },
      {
        path: 'facturas',
        loadComponent: () => import('./documentos/documentos.component').then(c => c.DocumentosComponent),
        data: { zona: 'facturas' },
      },
      { path: 'facturas/pedir', loadComponent: () => import('./facturar/facturar.component').then(c => c.FacturarComponent) },
      {
        path: 'dinero',
        loadComponent: () => import('./documentos/documentos.component').then(c => c.DocumentosComponent),
        data: { zona: 'dinero' },
      },
      { path: 'dinero/efectivo', loadComponent: () => import('./dinero/dinero.component').then(c => c.DineroComponent) },
      {
        path: 'control',
        loadComponent: () => import('./documentos/documentos.component').then(c => c.DocumentosComponent),
        data: { zona: 'control' },
      },
      {
        path: 'control/expedientes',
        loadComponent: () => import('./control-interno/control-interno.component').then(c => c.ControlInternoComponent),
      },
      { path: 'todo', loadComponent: () => import('./mi-informacion/todo.component').then(c => c.TodoComponent) },
    ],
  },
  {
    // La mesa de trabajo del despacho: el servidor la niega a quien no es del equipo (Autorizador).
    path: 'mesa',
    loadComponent: () => import('./mesa/mesa.component').then(c => c.MesaComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Organizaciones' },
  },
  {
    // La nómina del despacho: trabajadores y periodos de cada cliente (el servidor la niega a quien no es del equipo).
    path: 'nomina',
    loadComponent: () => import('./nomina/nomina.component').then(c => c.NominaComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Organizaciones' },
  },
  {
    // Análisis normativo del despacho (hipótesis normativa con el índice de leyes y Claude). El servidor lo niega fuera del equipo.
    path: 'normativa',
    loadComponent: () => import('./normativa/normativa.component').then(c => c.NormativaComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Organizaciones' },
  },
  // Las pantallas de antes viven ahora en «Mi información» (las ligas viejas siguen sirviendo; ?cliente= se conserva).
  { path: 'documentos', redirectTo: 'mi-informacion/facturas' },
  { path: 'facturar', redirectTo: 'mi-informacion/facturas/pedir' },
  { path: 'dinero', redirectTo: 'mi-informacion/dinero/efectivo' },
  { path: 'control-interno', redirectTo: 'mi-informacion/control/expedientes' },
  {
    // El servidor decide qué clientes ve cada quien y quién ve contraseñas (Autorizador).
    path: 'credenciales',
    loadComponent: () => import('./credenciales/credenciales.component').then(c => c.CredencialesComponent),
    canActivate: [authGuard],
  },
  {
    // Sin permiso: las tesis son públicas; el servidor decide quién puede actualizar la librería.
    path: 'sentencias',
    loadComponent: () => import('./sentencias/sentencias.component').then(c => c.SentenciasComponent),
    canActivate: [authGuard],
  },
  // Explorador de BD: solo en Development.
  ...(environment.production
    ? []
    : [
        {
          // El catálogo del design system: herramienta de desarrollo, sin permiso propio.
          path: 'design-system',
          loadComponent: () =>
            import('./design-system/design-system.component').then(c => c.DesignSystemComponent),
          canActivate: [authGuard],
        },
        {
          path: 'db-explorer',
          loadComponent: () => import('./db-explorer/db-explorer.component').then(c => c.DbExplorerComponent),
          canActivate: [authGuard, permissionGuard],
          data: { requiredPolicy: 'AuxiliarContable.DbExplorer' },
        },
      ]),
  {
    path: 'actividad',
    loadComponent: () => import('./actividad/actividad.component').then(c => c.ActividadComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Actividad' },
  },
  {
    // «Mi organización»: cada cliente la suya. Sin permiso: lo que puede hacer ahí lo decide su
    // papel en la organización (el servidor responde 404 a lo demás).
    path: 'mi-organizacion',
    loadComponent: () => import('./mi-organizacion/mi-organizacion.component').then(c => c.MiOrganizacionComponent),
    canActivate: [authGuard],
  },
  {
    // Solo el equipo: todos los clientes de un vistazo (y las métricas, solo el Owner).
    path: 'cartera',
    loadComponent: () => import('./cartera/cartera.component').then(c => c.CarteraComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Organizaciones' },
  },
  {
    // Conciliación mensual: el auxiliar carga todo el mes de un cliente y concilia (solo el despacho).
    path: 'conciliacion-mensual',
    loadComponent: () => import('./conciliacion-mensual/conciliacion-mensual.component').then(c => c.ConciliacionMensualComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Organizaciones' },
  },
  {
    // El cierre del mes. Sin permiso: el servidor decide qué clientes ve cada quien y solo el
    // despacho resuelve partidas y cierra (Autorizador).
    path: 'conciliacion',
    loadComponent: () => import('./conciliacion/conciliacion.component').then(c => c.ConciliacionComponent),
    canActivate: [authGuard],
  },
  {
    // Sin permiso: el servidor decide qué clientes ve cada quien (Autorizador).
    path: 'perfil-fiscal',
    loadComponent: () => import('./perfil-fiscal/perfil-fiscal.component').then(c => c.PerfilFiscalComponent),
    canActivate: [authGuard],
  },
  {
    path: 'organizaciones',
    loadComponent: () => import('./organizaciones/organizaciones.component').then(c => c.OrganizacionesComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Organizaciones' },
  },
  {
    // Mismo permiso que crear usuarios: invitar ES dar de alta a alguien.
    path: 'invitaciones',
    loadComponent: () => import('./invitaciones/invitaciones.component').then(c => c.InvitacionesComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AbpIdentity.Users.Create' },
  },
  {
    // Sin permiso: cada uno protege SU cuenta (correo, teléfono, dos pasos).
    path: 'seguridad',
    loadComponent: () => import('./seguridad/seguridad.component').then(c => c.SeguridadComponent),
    canActivate: [authGuard],
  },
  {
    // Sin permiso: cada uno ve el manual de su rol (y los de debajo).
    path: 'ayuda',
    loadComponent: () => import('./ayuda/ayuda.component').then(c => c.AyudaComponent),
    canActivate: [authGuard],
  },
  {
    // PÚBLICA: el aviso de privacidad se lee ANTES de tener cuenta (registro, login).
    path: 'privacidad',
    loadComponent: () => import('./privacidad/aviso-de-privacidad.component').then(c => c.AvisoDePrivacidadComponent),
  },
  {
    // PÚBLICA: la abre quien recibió la invitación y aún no tiene cuenta. Lo que protege
    // es el token del correo, no la sesión. El layout la pinta sin menú.
    path: 'registro',
    loadComponent: () => import('./registro/registro.component').then(c => c.RegistroComponent),
  },
  {
    path: 'metrics',
    loadComponent: () => import('./metrics/metrics.component').then(c => c.MetricsComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Metrics' },
  },
  {
    // La portada de «Mi cuenta». Sin permiso, como sus hermanas: cada uno ve LO SUYO y el
    // identificador sale de la sesión, nunca de la petición.
    path: 'mi-cuenta',
    loadComponent: () => import('./mi-cuenta/mi-cuenta.component').then(c => c.MiCuentaComponent),
    canActivate: [authGuard],
  },
  {
    // Sin permiso: cualquiera que haya iniciado sesion vincula SU cuenta, y solo la suya.
    path: 'telegram',
    loadComponent: () =>
      import('./telegram/telegram-link.component').then(c => c.TelegramLinkComponent),
    canActivate: [authGuard],
  },
  {
    // Sin permiso: cada uno ve SUS avisos, y de los dirigidos a un permiso solo llegan los
    // de los permisos que ya tiene. Ver NotificationAppService.
    path: 'notifications',
    loadComponent: () =>
      import('./notifications/notifications.component').then(c => c.NotificationsComponent),
    canActivate: [authGuard],
  },
  {
    path: 'system-status',
    loadComponent: () =>
      import('./system-status/system-status.component').then(c => c.SystemStatusComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.SystemStatus' },
  },
  {
    path: 'jobs',
    loadComponent: () => import('./jobs/jobs.component').then(c => c.JobsComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Jobs' },
  },
  {
    // ⚠️ Auditoria: el registro de peticiones y el historial de cambios. Vivian DENTRO de
    // /metrics, que medía 12,6 pantallas de scroll juntando graficos del servidor, trafico
    // bloqueado, auditoria y la configuracion del bot de Telegram. Mismo permiso que tenian
    // alli — no es un permiso nuevo, es la misma informacion en su sitio.
    path: 'audit',
    loadComponent: () => import('./audit/auditoria.component').then(c => c.AuditoriaComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.AuditLogs' },
  },
  {
    // A donde van los avisos del sistema. Tambien salia de /metrics: un formulario para
    // pegar el token de un bot no es una metrica.
    path: 'alertas',
    loadComponent: () => import('./alertas/alertas.component').then(c => c.AlertasComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Metrics.ManageSettings' },
  },
  {
    path: 'branding',
    loadComponent: () => import('./branding/branding.component').then(c => c.BrandingComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Branding' },
  },
  {
    path: 'security',
    loadComponent: () => import('./security/security.component').then(c => c.SecurityComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.Security.BlockedIps' },
  },
  {
    // CON permiso, al reves que la foto: una clave llama a la API sin navegador y sin
    // segundo factor, asi que quien puede crearlas deberia poder decidirse.
    path: 'api-keys',
    loadComponent: () => import('./api-keys/api-keys.component').then(c => c.ApiKeysComponent),
    canActivate: [authGuard, permissionGuard],
    data: { requiredPolicy: 'AuxiliarContable.ApiKeys' },
  },
  {
    // Sin permiso: cada uno ve SUS sesiones, y solo las suyas.
    path: 'sessions',
    loadComponent: () => import('./sessions/sessions.component').then(c => c.SessionsComponent),
    canActivate: [authGuard],
  },
  {
    // Sin permiso, por el mismo motivo: cada uno gestiona SU foto, y solo la suya.
    path: 'profile-picture',
    loadComponent: () =>
      import('./profile-picture/profile-picture.component').then(c => c.ProfilePictureComponent),
    canActivate: [authGuard],
  },
  {
    path: 'account',
    loadChildren: () => import('@abp/ng.account').then(c => c.createRoutes()),
  },
  {
    path: 'identity',
    loadChildren: () => import('@abp/ng.identity').then(c => c.createRoutes()),
  },
  {
    path: 'setting-management',
    loadChildren: () => import('@abp/ng.setting-management').then(c => c.createRoutes()),
  },
];
