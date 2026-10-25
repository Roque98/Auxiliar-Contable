import { ConfigStateService, RoutesService, eLayoutType } from '@abp/ng.core';
import { inject, provideAppInitializer } from '@angular/core';
import { environment } from '../environments/environment';
import { MiOrganizacionService } from './mi-organizacion/mi-organizacion.service';

/*
 * El menú va agrupado en dos ramas en vez de una lista plana:
 *
 *   Mi cuenta  -> lo que cada usuario gestiona de SÍ MISMO
 *   Sistema    -> lo que se mira o se administra de la INSTALACIÓN
 *
 * Con siete entradas sueltas al mismo nivel no se distinguía "mi foto de perfil" de
 * "métricas del servidor", que no se parecen en nada ni las usa la misma persona. La
 * separación es la misma que ya hacen los permisos.
 *
 * Un padre se declara con `path: undefined` —no navega a ninguna parte— y los hijos lo
 * referencian por `parentName`, igual que hacen los módulos del propio ABP.
 */
const MI_CUENTA = '::Menu:MiCuenta';
const SISTEMA = '::Menu:Sistema';

/**
 * Los MODULOS del negocio: lo que se usa a diario, no la instalación.
 *
 * ⚠️ Rama propia y no dentro de «Sistema»: Sistema es administrar la INSTALACIÓN —cola de
 * trabajos, seguridad, métricas—, y quien solo usa los módulos no tiene nada que ver ahí.
 *
 * Todos sus hijos llevan permiso, así que a quien no tenga ninguno la rama le desaparece.
 */
const MODULOS = '::Menu:Modulos';

export const APP_ROUTE_PROVIDER = [
  provideAppInitializer(() => {
    configureRoutes();
  }),
];

function configureRoutes() {
  const routes = inject(RoutesService);
  const config = inject(ConfigStateService);
  const miOrganizacion = inject(MiOrganizacionService);

  // Ni "Vincular Telegram" ni "Foto de perfil" llevan requiredPolicy —cada usuario
  // gestiona LO SUYO, sin permiso de por medio— pero sin policy ABP las muestra siempre,
  // también a quien no ha iniciado sesión. Se registran invisibles y se encienden al
  // haber sesión.
  //
  // El PADRE también: si no, un anónimo vería "Mi cuenta" abriendo un submenú vacío.
  //
  // Esto es solo presentación: las rutas ya están protegidas por authGuard, así que un
  // anónimo que las escriba a mano acaba en el login igualmente.
  config.getOne$('currentUser').subscribe(usuario => {
    const anonimo = !usuario?.isAuthenticated;
    routes.patch(MI_CUENTA, { invisible: anonimo });

    // Los iconos de las rutas que registra ABP, al estilo de trazo del design system. Va
    // aquí y no arriba porque sus módulos las añaden en su propio arranque, que puede
    // correr después de este.
    routes.patch('AbpUiNavigation::Menu:Administration', { iconClass: 'bi bi-sliders', order: 80 });
    routes.patch('AbpIdentity::Menu:IdentityManagement', { iconClass: 'bi bi-people' });
    routes.patch('AbpIdentity::Users', { iconClass: 'bi bi-people', order: 1 });
    routes.patch('AbpIdentity::Roles', { iconClass: 'bi bi-shield-lock', order: 2 });
    routes.patch('::Menu:Ayuda', { invisible: anonimo });
    // Oculta: sus pestañas (correo SMTP y zona horaria) no aplican aquí y sus permisos
    // están apagados (AuxiliarContablePermissionDefinitionProvider.PostDefine); la pantalla
    // quedaría vacía. El correo se configura en el servidor (Brevo).
    routes.patch('AbpSettingManagement::Settings', { invisible: true });
    // ⚠️ Cada entrada sin `requiredPolicy` necesita SU patch. Se registran invisibles y esta
    // lista es lo único que las enciende: a la portada se le olvidó al añadirla y
    // simplemente no salía en el menú — sin error, sin traza, y con la clave de traducción
    // resolviendo perfectamente. Se vio listando el menú del navegador.
    routes.patch('::Cuenta:Resumen', { invisible: anonimo });
    routes.patch('::Menu:Telegram', { invisible: anonimo });
    routes.patch('::Menu:ProfilePicture', { invisible: anonimo });
    routes.patch('::Menu:Sessions', { invisible: anonimo });
    routes.patch('::Menu:SeguridadCuenta', { invisible: anonimo });
    routes.patch('::Menu:Notifications', { invisible: anonimo });

    // «Mi organización», solo a quien pertenece a alguna (los clientes). No hay permiso que
    // lo diga: se pregunta. Para el equipo la lista viene vacía y la entrada no sale.
    routes.patch('::Menu:MiOrganizacion', { invisible: true });
    if (!anonimo) {
      miOrganizacion.getList().subscribe({
        next: lista => routes.patch('::Menu:MiOrganizacion', { invisible: !lista.length }),
        error: () => undefined,
      });
    }
  });

  routes.add([
    {
      path: '/',
      name: '::Menu:Home',
      iconClass: 'bi bi-house',
      order: 1,
      layout: eLayoutType.application,
    },

    // El perfil fiscal: para el cliente, el suyo; para el despacho, el de cada cliente. Sin
    // permiso propio: quién ve y cambia qué lo decide el servidor (Autorizador, 404 a lo ajeno).
    {
      path: '/perfil-fiscal',
      name: '::Menu:PerfilFiscal',
      iconClass: 'bi bi-person-vcard',
      order: 2,
      layout: eLayoutType.application,
    },

    // --- Mi cuenta -------------------------------------------------------------
    //
    // ⚠️⚠️ El padre se queda SIN ruta, y las dos razones importan.
    //
    // 1. LeptonX trata un padre con hijos como un desplegable y **se salta su `path`**:
    //    comprobado en el navegador — pulsar «Mi cuenta» abre el submenú y la URL no cambia.
    // 2. Peor: declarando la MISMA ruta en el padre y en el hijo, el hijo **no llega a
    //    pintarse**. También comprobado — «Resumen» no salía en el menú y no fallaba nada.
    //
    // Así que el grupo agrupa y la portada entra como su primera entrada, que es además lo
    // que hacen GitHub o Google. Antes esto era un grupo de cinco hermanas y cada una abría
    // una tarjeta suelta en una pantalla en blanco.
    {
      path: undefined,
      name: MI_CUENTA,
      iconClass: 'bi bi-person-circle',
      order: 20,
      layout: eLayoutType.application,
      // Arranca oculto; la suscripción de arriba lo muestra al haber sesión.
      invisible: true,
    },
    {
      // La portada de la cuenta. Ver el comentario del padre para por qué la ruta vive aquí
      // y no allí.
      path: '/mi-cuenta',
      name: '::Cuenta:Resumen',
      parentName: MI_CUENTA,
      iconClass: 'bi bi-person-badge',
      order: 0,
      layout: eLayoutType.application,
      invisible: true,
    },
    {
      path: '/seguridad',
      name: '::Menu:SeguridadCuenta',
      parentName: MI_CUENTA,
      iconClass: 'bi bi-shield-lock',
      order: 0.5,
      layout: eLayoutType.application,
      invisible: true,
    },
    {
      path: '/sessions',
      name: '::Menu:Sessions',
      parentName: MI_CUENTA,
      iconClass: 'bi bi-laptop',
      order: 1,
      layout: eLayoutType.application,
      invisible: true,
    },
    {
      path: '/profile-picture',
      name: '::Menu:ProfilePicture',
      parentName: MI_CUENTA,
      iconClass: 'bi bi-image',
      order: 3,
      layout: eLayoutType.application,
      invisible: true,
    },
    {
      path: '/telegram',
      name: '::Menu:Telegram',
      parentName: MI_CUENTA,
      iconClass: 'bi bi-telegram',
      order: 2,
      layout: eLayoutType.application,
      invisible: true,
    },
    {
      // La campana de la barra superior lleva aquí, pero la entrada de menú también hace
      // falta: en móvil la barra se pliega y la campana deja de estar a la vista.
      path: '/notifications',
      name: '::Menu:Notifications',
      parentName: MI_CUENTA,
      iconClass: 'bi bi-bell',
      order: 4,
      layout: eLayoutType.application,
      invisible: true,
    },
    {
      path: '/api-keys',
      name: '::Menu:ApiKeys',
      parentName: MI_CUENTA,
      iconClass: 'bi bi-key',
      order: 5,
      layout: eLayoutType.application,
      // Lleva permiso propio, así que ABP ya la oculta a quien no lo tiene: no necesita
      // el apaño de 'invisible' de sus hermanas.
      requiredPolicy: 'AuxiliarContable.ApiKeys',
    },

    // Junto a Usuarios y Roles: invitar es la forma normal de dar de alta a alguien.
    {
      path: '/invitaciones',
      name: '::Menu:Invitaciones',
      parentName: 'AbpIdentity::Menu:IdentityManagement',
      iconClass: 'bi bi-envelope-plus',
      order: 3,
      layout: eLayoutType.application,
      requiredPolicy: 'AbpIdentity.Users.Create',
    },

    // Los clientes del despacho y sus personas. Decide quién ve qué.
    {
      path: '/organizaciones',
      name: '::Menu:Organizaciones',
      parentName: 'AbpIdentity::Menu:IdentityManagement',
      iconClass: 'bi bi-building',
      order: 4,
      layout: eLayoutType.application,
      requiredPolicy: 'AuxiliarContable.Organizaciones',
    },

    // La organización de cada cliente: su gente y su papel. Se enciende arriba si pertenece a alguna.
    {
      path: '/mi-organizacion',
      name: '::Menu:MiOrganizacion',
      parentName: MI_CUENTA,
      iconClass: 'bi bi-building',
      order: 0,
      layout: eLayoutType.application,
      invisible: true,
    },

    // Quién hizo qué: para el equipo (el Developer no aparece nunca).
    {
      path: '/actividad',
      name: '::Menu:Actividad',
      parentName: 'AbpIdentity::Menu:IdentityManagement',
      iconClass: 'bi bi-clock-history',
      order: 5,
      layout: eLayoutType.application,
      requiredPolicy: 'AuxiliarContable.Actividad',
    },

    // El manual: cada uno ve el de su rol. Sin permiso, pero solo con sesión.
    {
      path: '/ayuda',
      name: '::Menu:Ayuda',
      iconClass: 'bi bi-question-circle',
      order: 95,
      layout: eLayoutType.application,
    },

    // --- Modulos del cliente ---------------------------------------------------
    {
      path: undefined,
      name: MODULOS,
      iconClass: 'bi bi-grid',
      order: 50,
      layout: eLayoutType.application,
      // Sin 'invisible': sus hijos llevan permiso, asi que quien no tenga ninguno no ve
      // la rama.
    },

    // --- Sistema ---------------------------------------------------------------
    {
      path: undefined,
      name: SISTEMA,
      iconClass: 'bi bi-hdd-stack',
      order: 90,
      layout: eLayoutType.application,
      // Sin 'invisible': todos sus hijos llevan permiso, así que a quien no tenga ninguno
      // no le queda nada que ver debajo.
    },
    {
      path: '/system-status',
      name: '::Menu:SystemStatus',
      parentName: SISTEMA,
      iconClass: 'bi bi-activity',
      order: 0,
      layout: eLayoutType.application,
      requiredPolicy: 'AuxiliarContable.SystemStatus',
    },
    {
      path: '/metrics',
      name: '::Menu:Metrics',
      parentName: SISTEMA,
      iconClass: 'bi bi-graph-up',
      order: 1,
      layout: eLayoutType.application,
      requiredPolicy: 'AuxiliarContable.Metrics',
    },
    {
      path: '/audit',
      name: '::Menu:Audit',
      parentName: SISTEMA,
      iconClass: 'bi bi-journal-text',
      order: 15,
      layout: eLayoutType.application,
      requiredPolicy: 'AuxiliarContable.AuditLogs',
    },
    {
      path: '/alertas',
      name: '::Menu:Alertas',
      parentName: SISTEMA,
      iconClass: 'bi bi-bell',
      order: 16,
      layout: eLayoutType.application,
      requiredPolicy: 'AuxiliarContable.Metrics.ManageSettings',
    },
    {
      path: '/security',
      name: '::Menu:Security',
      parentName: SISTEMA,
      iconClass: 'bi bi-shield-check',
      order: 2,
      layout: eLayoutType.application,
      requiredPolicy: 'AuxiliarContable.Security.BlockedIps',
    },
    {
      path: '/branding',
      name: '::Menu:Branding',
      parentName: SISTEMA,
      iconClass: 'bi bi-palette',
      order: 4,
      layout: eLayoutType.application,
      requiredPolicy: 'AuxiliarContable.Branding',
    },
    {
      path: '/jobs',
      name: '::Menu:Jobs',
      parentName: SISTEMA,
      iconClass: 'bi bi-list-task',
      order: 5,
      layout: eLayoutType.application,
      requiredPolicy: 'AuxiliarContable.Jobs',
    },
    // Explorador de BD: solo en Development.
    ...(environment.production
      ? []
      : [
          {
            path: '/db-explorer',
            name: '::Menu:DbExplorer',
            parentName: SISTEMA,
            iconClass: 'bi bi-table',
            order: 6,
            layout: eLayoutType.application,
            requiredPolicy: 'AuxiliarContable.DbExplorer',
          },
          {
            path: '/design-system',
            name: 'Design system',
            parentName: SISTEMA,
            iconClass: 'bi bi-palette2',
            order: 7,
            layout: eLayoutType.application,
            requiredPolicy: 'AuxiliarContable.DbExplorer',
          },
        ]),
  ]);
}
