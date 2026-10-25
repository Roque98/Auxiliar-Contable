using System;
using System.Collections.Generic;

namespace AuxiliarContable.MiCuenta;

/// <summary>
/// Lo que la pantalla de «Mi cuenta» necesita saber de quien la está mirando.
///
/// <para>⚠️ <b>Qué NO va aquí, y por qué.</b> El nombre, el usuario, el correo y los roles ya
/// viajan en <c>/api/abp/application-configuration</c> (<c>currentUser</c>), que el SPA pide
/// al arrancar y tiene en memoria. Repetirlos en esta respuesta daría <b>dos fuentes para el
/// mismo dato</b> y un momento —entre que cambia una y se recarga la otra— en el que la
/// pantalla se contradice a sí misma. Aquí va solo lo que ABP no cuenta.</para>
/// </summary>
public class MiCuentaDto
{
    /// <summary>Desde cuándo existe la cuenta. Es la única fecha que da contexto al resto.</summary>
    public DateTime CreacionUtc { get; set; }

    /// <summary>
    /// Último inicio de sesión <b>anterior a este</b>, sacado del registro de seguridad.
    ///
    /// <para>⚠️ Se excluye la entrada de la sesión en curso a propósito: enseñar «último
    /// acceso: hace 2 segundos» es enseñarle a alguien su propio reflejo. Lo que este dato
    /// contesta es «¿fui yo la última vez?», y para eso hace falta la anterior. Es nulo la
    /// primera vez que se entra.</para>
    /// </summary>
    public DateTime? AccesoAnteriorUtc { get; set; }

    /// <summary>Correo confirmado. Sin esto, la recuperación de cuenta no es fiable.</summary>
    public bool CorreoConfirmado { get; set; }

    public string? Telefono { get; set; }

    /// <summary>Segundo factor activo. Va junto a Telegram porque es su único proveedor.</summary>
    public bool SegundoFactorActivo { get; set; }

    public bool TelegramVinculado { get; set; }

    /// <summary>Sesiones abiertas, la de ahora incluida.</summary>
    public int SesionesActivas { get; set; }

    /// <summary>Claves de API <b>utilizables</b>: ni revocadas ni caducadas (§6.966.07).</summary>
    public int ClavesDeApi { get; set; }

    public int AvisosSinLeer { get; set; }

    /// <summary>
    /// Nombres de los roles. Sí se repiten con `currentUser` —es la excepción a la regla de
    /// arriba— porque ahí llegan como códigos y la pantalla los enseña tal cual.
    /// </summary>
    public List<string> Roles { get; set; } = [];
}
