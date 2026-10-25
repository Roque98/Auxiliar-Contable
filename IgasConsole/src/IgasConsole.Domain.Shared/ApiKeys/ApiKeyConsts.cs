namespace IgasConsole.ApiKeys;

public static class ApiKeyConsts
{
    /// <summary>Cabecera por la que viaja la clave.</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>
    /// Prefijo visible de toda clave. Sirve para reconocerla de un vistazo: si aparece en
    /// un log, un repositorio o un pantallazo, se sabe al instante QUÉ es lo que se ha
    /// filtrado y hay que revocar. Sin prefijo, una cadena aleatoria no dice nada.
    /// </summary>
    public const string Prefijo = "tpl_";

    /// <summary>Bytes aleatorios de la clave. 32 bytes = 256 bits.</summary>
    public const int KeyBytes = 32;

    /// <summary>SHA-256 en hexadecimal.</summary>
    public const int HashLength = 64;

    /// <summary>
    /// Cuántos caracteres del principio se guardan en claro para poder listarlas.
    /// <para>Incluye el prefijo. Es poca información: con 8 caracteres no se reconstruye
    /// una clave de 256 bits, pero basta para distinguir dos claves en una tabla.</para>
    /// </summary>
    public const int PistaLength = 12;

    public const int MaxNameLength = 128;

    /// <summary>
    /// Cada cuántos minutos, como mucho, se actualiza la marca de último uso.
    /// <para>Escribirla en cada petición convertiría cada llamada de solo lectura en una
    /// escritura. Con este margen la marca sirve para lo que hace falta —saber si una clave
    /// sigue viva antes de revocarla— sin castigar el camino caliente.</para>
    /// </summary>
    public const int UltimoUsoRefrescoMinutos = 5;

    /// <summary>
    /// Claim que marca que la peticion llego con clave de API, y con cual.
    ///
    /// <para>Hace falta porque el principal se construye con la MISMA fabrica que el login
    /// (para que los permisos sean identicos), y por tanto sale indistinguible de una
    /// sesion de navegador. Sin esta marca, el limite por identidad no podria dar a cada
    /// clave su propia cuota y todas compartirian la de su dueno — un script desbocado
    /// dejaria sin servicio a los demas scripts de esa persona y a su propia sesion.</para>
    /// </summary>
    public const string ClaimTipo = "template_api_key";

    /// <summary>
    /// Con cuántos días de antelación se avisa de que una clave va a caducar.
    /// <para>Siete: lo bastante para cambiarla sin prisa, y lo bastante cerca para que el
    /// aviso siga significando algo cuando llegue.</para>
    /// </summary>
    public const int AvisoCaducidadDias = 7;

    /// <summary>
    /// Cuánto se calla el vigilante tras avisar de la misma clave.
    /// <para>Dos días, no siete: así dentro de la ventana de aviso llegan un par de
    /// recordatorios en vez de uno solo que puede pasarse por alto — sin convertirse en el
    /// aviso diario que se acaba ignorando.</para>
    /// </summary>
    public const int AvisoCaducidadSilencioDias = 2;

    /// <summary>Cada cuántas horas mira el vigilante.</summary>
    public const int AvisoCaducidadIntervaloHoras = 12;
}
