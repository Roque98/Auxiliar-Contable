/**
 * El manual de la consola. Es contenido, no lógica: está aquí y no en la base para que viaje
 * con el código que describe (si cambia una pantalla, cambia en el mismo commit).
 *
 * ⚠️ Cada tema dice QUÉ PERMISO hace falta para lo que explica, y solo se enseña a quien lo
 * tiene. No va por rol: el Owner puede cambiar los permisos de cada rol, y un manual por rol
 * explicaría pantallas que esa persona ya no ve (o callaría las que sí).
 */

export interface Tema {
  titulo: string;
  icono: string;
  /** Párrafos cortos. */
  texto?: string[];
  /** Pasos numerados. */
  pasos?: string[];
  /** Un consejo o una advertencia al final. */
  nota?: string;
  /** Pantalla a la que lleva, si la hay. */
  ruta?: string;
  /** Basta con UNO de estos permisos. Sin permisos: para todo el mundo. */
  permisos?: string[];
  /**
   * Se esconde a quien tenga ALGUNO de estos. Para lo que solo aplica al cliente: al equipo
   * (que ve a todos los clientes) le explicaría algo que no le pasa.
   */
  salvoPermisos?: string[];
  /** Nivel mínimo en la jerarquía (3 = Developer). Solo para lo que no depende de un permiso. */
  nivelMinimo?: number;
}

export interface Seccion {
  titulo: string;
  temas: Tema[];
}

export const RESUMEN_POR_NIVEL: Record<number, { rol: string; texto: string }> = {
  0: { rol: 'Usuario', texto: 'Ves y subes la información de tu organización (según tu papel en ella) y gestionas tu propia cuenta.' },
  1: { rol: 'Administrador', texto: 'Ves la información de todos los clientes, los organizas y das de alta a los Usuarios.' },
  2: { rol: 'Owner', texto: 'Eres el dueño del negocio: gestionas a tu gente, sus roles y la imagen de la consola.' },
  3: { rol: 'Developer', texto: 'Mantienes la consola: tienes todos los permisos y nadie más ve tu cuenta.' },
};

export const SECCIONES: Seccion[] = [
  {
    titulo: 'Lo básico',
    temas: [
      {
        titulo: 'Entrar por primera vez',
        icono: 'bi-box-arrow-in-right',
        pasos: [
          'Abre el enlace del correo «Tu invitación a Auxiliar contable». Vale 7 días y sirve una sola vez.',
          'Elige tu usuario y tu contraseña. Al hacerlo tu correo queda confirmado.',
          'Entra con tu usuario (o tu correo) y la contraseña que elegiste.',
        ],
        nota: '¿Te dieron la cuenta sin invitación? Entra con el usuario y la contraseña que te pasaron y cámbiala en «Seguridad».',
      },
      {
        titulo: 'Protege tu cuenta',
        icono: 'bi-shield-lock',
        ruta: '/seguridad',
        pasos: [
          'Ve a Mi cuenta → Seguridad.',
          'Registra tu correo y escribe el código que te llega para verificarlo.',
          'Registra tu celular con código de país (por ejemplo +52 55 1234 5678).',
          'Activa la verificación en dos pasos: al entrar, además de la contraseña, te pediremos un código.',
          'Guarda los 10 códigos de recuperación que aparecen al activarla: cada uno te deja entrar una vez si no te llega el código.',
        ],
        nota: 'Nadie del despacho te va a pedir un código por teléfono ni mensaje. Si alguien lo hace, no se lo des.',
      },
      {
        titulo: 'Si olvidas la contraseña',
        icono: 'bi-key',
        pasos: [
          'En la pantalla de entrar, pulsa «¿Olvidaste tu contraseña?» y escribe tu correo.',
          'Te llega un correo con un botón para elegir una nueva. El enlace caduca pronto y sirve una vez.',
        ],
        nota: 'Si no tienes correo verificado, pide a quien te dio de alta que te ponga una contraseña nueva.',
      },
      {
        titulo: 'Tu cuenta',
        icono: 'bi-person-circle',
        ruta: '/mi-cuenta',
        texto: [
          'Desde el menú de tu nombre llegas a Mi cuenta: tus datos, tu seguridad, tu foto, tus sesiones y tus avisos.',
          'En Sesiones ves desde qué equipos has entrado y puedes cerrar las que no reconozcas.',
        ],
      },
      {
        titulo: 'Trucos',
        icono: 'bi-lightbulb',
        texto: [
          'Ctrl + K (⌘ + K en Mac) abre el buscador para saltar a cualquier pantalla.',
          'El botón del sol / la luna, arriba a la derecha, cambia entre modo claro y oscuro.',
        ],
      },
    ],
  },
  {
    titulo: 'Actividad',
    temas: [
      {
        titulo: 'Ver quién hizo qué',
        icono: 'bi-clock-history',
        ruta: '/actividad',
        permisos: ['AuxiliarContable.Actividad'],
        texto: [
          'En Actividad está, día por día, lo que hizo cada quien: quién entró, a quién invitó y qué cambió en las organizaciones. Filtra por persona, por organización o por tipo.',
          'Lo repetido seguido (editar lo mismo varias veces) sale una vez con «×».',
          'Las acciones se guardan 30 días; las invitaciones aceptadas quedan siempre.',
        ],
      },
    ],
  },
  {
    titulo: 'Organizaciones',
    temas: [
      {
        titulo: 'Gestionar a la gente de tu organización',
        icono: 'bi-building',
        ruta: '/mi-organizacion',
        salvoPermisos: ['AuxiliarContable.Organizaciones'],
        texto: [
          'En Mi cuenta → Mi organización ves tu organización y tu papel en ella.',
          'Si eres su Administrador, ahí invitas a personas de tu empresa, cambias su papel o las quitas. Cada papel: Administrador (además gestiona a la gente), Miembro (sube y ve) y Solo ve.',
        ],
        nota: 'Una organización no puede quedarse sin administrador: nombra a otro antes de dejar de serlo.',
      },
      {
        titulo: 'Dar acceso a un cliente',
        icono: 'bi-building',
        ruta: '/organizaciones',
        permisos: ['AuxiliarContable.Organizaciones'],
        pasos: [
          'Ve a Organizaciones y crea una con el nombre del cliente (una persona física, una empresa o un grupo de empresas).',
          'Invita a la persona del cliente con el rol Usuario y elige su organización y su papel en ella en la misma invitación (o créala ahí con «Nueva»). Al aceptar, entra ya dentro.',
          'Nombra a una persona del cliente Administrador de su organización (en la lista de Personas): desde «Mi organización» invitará y gestionará a su propia gente, sin depender de ti.',
        ],
        nota: 'Una cuenta de cliente siempre se crea dentro de una organización: Invitaciones y Usuarios no dejan continuar sin elegirla. Quitar a alguien de la organización le corta el acceso al momento. El equipo del despacho (Owner y Administradores) ve todo lo de las organizaciones sin pertenecer a ninguna.',
      },
    ],
  },
  {
    titulo: 'Personas',
    temas: [
      {
        titulo: 'Invitar a alguien',
        icono: 'bi-envelope-plus',
        ruta: '/invitaciones',
        permisos: ['AbpIdentity.Users.Create'],
        pasos: [
          'Ve a Administración → Invitaciones (o «Invitar» en Usuarios).',
          'Elige el rol —solo aparecen los que están por debajo del tuyo— y escribe su correo.',
          'Pulsa «Enviar invitación». Le llega un enlace para crear su cuenta.',
          'Sigue el estado en la lista: Pendiente, Aceptada, Vencida o Revocada. «Reenviar» manda un enlace nuevo.',
        ],
        nota: 'Invitar es mejor que crear la cuenta a mano: no tienes que inventar contraseñas y el correo queda verificado.',
      },
      {
        titulo: 'Crear una cuenta sin correo',
        icono: 'bi-person-plus',
        ruta: '/identity/users',
        permisos: ['AbpIdentity.Users.Create'],
        pasos: [
          'En Usuarios pulsa «Crear a mano» y marca «Aún no tiene correo».',
          'Pon su usuario, su rol y una contraseña temporal, y pásasela en persona.',
          'Al entrar, la consola le pedirá registrar y verificar su correo y su teléfono.',
        ],
      },
      {
        titulo: 'Gestionar usuarios',
        icono: 'bi-person-lines-fill',
        ruta: '/identity/users',
        permisos: ['AbpIdentity.Users.Update'],
        texto: [
          'En Usuarios editas los datos de cada persona, su rol y su contraseña. Solo ves y tocas a quien está por debajo de ti.',
          'Para pausar el acceso de alguien sin perder su historial, desmarca «Activo» en vez de eliminarlo.',
        ],
      },
      {
        titulo: 'Si alguien no puede entrar',
        icono: 'bi-shield-x',
        ruta: '/identity/users',
        permisos: ['AbpIdentity.Users.Update'],
        pasos: [
          'Si olvidó la contraseña: en Usuarios, «Editar» y escríbele una nueva.',
          'Si tiene la verificación en dos pasos y no le llega el código ni tiene códigos de recuperación: al editarla, en «Seguridad», pulsa «Quitar verificación en dos pasos».',
        ],
        nota: 'Hazlo solo si confirmaste que es esa persona quien lo pide: es justo lo que intentaría quien quiere robarle la cuenta. Queda registrado.',
      },
      {
        titulo: 'Permisos de una persona',
        icono: 'bi-key',
        ruta: '/identity/users',
        permisos: ['AbpIdentity.Users.ManagePermissions'],
        texto: ['El botón de la llave en Usuarios da permisos a una sola persona, además de los de su rol. Úsalo para excepciones.'],
      },
    ],
  },
  {
    titulo: 'Roles y marca',
    temas: [
      {
        titulo: 'Roles y permisos',
        icono: 'bi-shield-lock',
        ruta: '/identity/roles',
        permisos: ['AbpIdentity.Roles.Create', 'AbpIdentity.Roles.ManagePermissions'],
        pasos: [
          'En Administración → Roles ves los roles que puedes gestionar.',
          'Para un puesto nuevo (por ejemplo «Analista» o «Supervisor»), crea un rol y elige su NIVEL: Usuario no gestiona a nadie; Administrador gestiona a los Usuarios; Owner, a Administradores y Usuarios.',
          'En «Opciones → Permisos» marca qué puede hacer.',
          'Asígnalo al invitar o al editar a una persona.',
        ],
        nota: 'Solo puedes crear roles de un nivel por debajo del tuyo y dar permisos que tú tienes. El nivel de los roles propios (Owner, Administrador, Usuario) no se cambia.',
      },
      {
        titulo: 'La marca',
        icono: 'bi-palette',
        ruta: '/branding',
        permisos: ['AuxiliarContable.Branding'],
        texto: ['En Sistema → Marca subes el logo y eliges el color principal. Se aplica a la consola y a la pantalla de entrar.'],
      },
    ],
  },
  {
    titulo: 'Operación',
    temas: [
      {
        titulo: 'Invisible para los demás',
        icono: 'bi-eye-slash',
        nivelMinimo: 3,
        texto: [
          'Tu cuenta no aparece en ninguna lista, ni tus auditorías, invitaciones o roles. Si alguien la pide por la API recibe «no existe».',
          'Las invitaciones que mandas no llevan tu nombre.',
        ],
      },
      {
        titulo: 'Estado, métricas y trabajos',
        icono: 'bi-hdd-stack',
        ruta: '/system-status',
        permisos: ['AuxiliarContable.SystemStatus', 'AuxiliarContable.Jobs', 'AuxiliarContable.Metrics'],
        texto: [
          'Sistema reúne Estado, Métricas, Auditoría, Alertas, Seguridad (IPs bloqueadas) y Trabajos en segundo plano.',
          'Si un correo no llega, mira Trabajos: el envío se reintenta solo y ahí queda el último error de Brevo.',
        ],
      },
    ],
  },
];

/** Qué puede hacer cada rol, tal como nacen (ver PermisosInicialesPorRol en el backend). */
export const MATRIZ: { accion: string; usuario: boolean; administrador: boolean; owner: boolean }[] = [
  { accion: 'Ver la información de su organización (según su papel en ella)', usuario: true, administrador: true, owner: true },
  { accion: 'Gestionar a la gente de su organización (si es su Administrador)', usuario: true, administrador: true, owner: true },
  { accion: 'Ver y gestionar a todos los clientes (organizaciones) y sus personas', usuario: false, administrador: true, owner: true },
  { accion: 'Ver la actividad (quién hizo qué)', usuario: false, administrador: true, owner: true },
  { accion: 'Invitar y gestionar Usuarios', usuario: false, administrador: true, owner: true },
  { accion: 'Invitar y gestionar Administradores', usuario: false, administrador: false, owner: true },
  { accion: 'Crear roles y decidir sus permisos', usuario: false, administrador: false, owner: true },
  { accion: 'Cambiar la marca (logo y color)', usuario: false, administrador: false, owner: true },
];

/** El nivel de quien tiene estos roles: el de su rol más alto. */
export function nivelDeRoles(roles: string[] | undefined | null): number {
  const niveles = (roles ?? []).map(r => {
    switch (r.toLowerCase()) {
      case 'admin':
        return 3;
      case 'owner':
        return 2;
      case 'administrador':
        return 1;
      default:
        return 0;
    }
  });
  return Math.max(0, ...niveles);
}

/** Las secciones con solo los temas que esta persona puede usar; las vacías desaparecen. */
export function manualPara(tienePermiso: (p: string) => boolean, nivel: number): Seccion[] {
  return SECCIONES.map(s => ({
    ...s,
    temas: s.temas.filter(
      t =>
        (!t.permisos || t.permisos.some(tienePermiso)) &&
        !(t.salvoPermisos ?? []).some(tienePermiso) &&
        (t.nivelMinimo === undefined || nivel >= t.nivelMinimo),
    ),
  })).filter(s => s.temas.length > 0);
}
