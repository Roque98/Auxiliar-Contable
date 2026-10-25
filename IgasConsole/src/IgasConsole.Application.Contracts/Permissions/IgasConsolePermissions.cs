namespace IgasConsole.Permissions;

/// <summary>
/// Permisos de la aplicación.
/// <para>No hace falta sembrarlos: ABP trae <c>PermissionDataSeedContributor</c>, que
/// concede al rol <c>admin</c> TODOS los permisos definidos (los de sus módulos y los de
/// aquí). Escribir otro sembrador para lo mismo duplica filas en
/// <c>AbpPermissionGrants</c>: ambos calculan lo que falta contra el mismo estado sin
/// volcar dentro de la misma transacción, y ambos insertan.</para>
/// </summary>
public static class IgasConsolePermissions
{
    public const string GroupName = "IgasConsole";


    public static class Metrics
    {
        public const string Default = GroupName + ".Metrics";

        /// <summary>Editar el bot y los chat id de los avisos. Separado de ver las
        /// métricas: mirar un gráfico y cambiar a dónde van las alertas no son lo mismo.</summary>
        public const string ManageSettings = Default + ".ManageSettings";
    }

    /// <summary>
    /// Registro de peticiones (auditoría de ABP). Aparte de <see cref="Metrics"/> a
    /// propósito: las métricas son números agregados del servidor, esto es quién hizo
    /// qué, desde qué IP y con qué usuario. Ver un gráfico de CPU y poder rastrear la
    /// actividad de una persona no son el mismo permiso.
    /// </summary>
    public static class AuditLogs
    {
        public const string Default = GroupName + ".AuditLogs";
    }

    /// <summary>
    /// Cola de trabajos en segundo plano. Aparte de <see cref="Metrics"/> porque no es un
    /// número agregado del servidor: los argumentos de un trabajo dicen sobre QUIÉN se iba
    /// a actuar (a qué usuario se le mandaba el enlace de recuperación), así que leer la
    /// cola se parece más al registro de peticiones que a un gráfico de CPU.
    /// </summary>
    public static class Jobs
    {
        public const string Default = GroupName + ".Jobs";

        /// <summary>Reintentar y borrar. Separado de verlos: mirar por qué falló algo y
        /// volver a lanzarlo —que puede mandar un correo, o cobrar— no son lo mismo.</summary>
        public const string Manage = Default + ".Manage";
    }

    /// <summary>
    /// Panel de estado del sistema: salud, copias de seguridad, trabajos y seguridad de un
    /// vistazo. Es el permiso de «mirar cómo va todo», y por eso solo enseña RECUENTOS —
    /// el detalle de cada cosa sigue detrás de su propio permiso.
    /// </summary>
    public static class SystemStatus
    {
        public const string Default = GroupName + ".SystemStatus";
    }

    /// <summary>
    /// Marca del inquilino: nombre, logo y color. Permiso propio y no reutilizado de otro
    /// porque su alcance es distinto a todo lo demás — quien lo tiene cambia lo que ven
    /// TODOS los usuarios de su inquilino, incluida la pantalla de login, pero no puede
    /// leer un dato de nadie.
    /// </summary>
    public static class Branding
    {
        public const string Default = GroupName + ".Branding";
    }

    /// <summary>
    /// Organizaciones (los clientes del despacho) y sus personas. Quien lo tiene es del EQUIPO:
    /// ve y gestiona a todos los clientes. Quien no, solo ve las organizaciones de las que es
    /// miembro. Crear, editar y asignar gente respeta la jerarquía: solo se añade a quien uno
    /// alcanza.
    /// </summary>
    public static class Organizaciones
    {
        public const string Default = GroupName + ".Organizaciones";
    }

    /// <summary>
    /// Actividad: quién hizo qué en la consola (entró, subió, vio, descargó, invitó…), contado
    /// en frases. Solo de las personas que uno alcanza: el Developer no aparece nunca.
    /// </summary>
    public static class Actividad
    {
        public const string Default = GroupName + ".Actividad";
    }

    /// <summary>Explorador de BD (herramienta de administración, solo Development).</summary>
    public static class DbExplorer
    {
        public const string Default = GroupName + ".DbExplorer";
        public const string Edit = Default + ".Edit";
    }

    /// <summary>
    /// Claves de API. Cada usuario gestiona las suyas, pero el permiso existe para poder
    /// decidir QUIEN puede crearlas: una clave llama a la API sin navegador, sin sesion y
    /// sin segundo factor, asi que no es lo mismo que cambiarse la foto de perfil.
    /// </summary>
    public static class ApiKeys
    {
        public const string Default = GroupName + ".ApiKeys";
    }

    /// <summary>
    /// Seguridad: quien puede entrar y quien no. Grupo aparte de Metrics a proposito —
    /// mirar un grafico de CPU y dejar a alguien fuera de la aplicacion no se parecen en
    /// nada, y meter ambos bajo el mismo permiso obligaba a conceder uno para dar el otro.
    /// </summary>
    public static class Security
    {
        /// <summary>Ver la lista de IPs bloqueadas.</summary>
        public const string BlockedIps = GroupName + ".Security.BlockedIps";

        /// <summary>Bloquear y desbloquear. Separado de verlas: consultar quien esta
        /// vetado y decidir quien lo esta no son la misma responsabilidad.</summary>
        public const string ManageBlockedIps = BlockedIps + ".Manage";

        /// <summary>
        /// Registro de seguridad: quien intento entrar, desde donde y si lo consiguio.
        /// Permiso propio porque es lo mas sensible de todo el panel — es el rastro de la
        /// actividad de personas concretas, no una lista de direcciones.
        /// </summary>
        public const string Logs = GroupName + ".Security.Logs";
    }
}
