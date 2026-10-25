using System.Threading.Tasks;

namespace AuxiliarContable.Security;

/// <summary>
/// Copia en memoria de las IPs bloqueadas, que es contra la que decide el middleware.
///
/// <para>El bloqueo se comprueba en CADA petición: consultar la base cada vez añadiría una
/// consulta a todo el tráfico, incluido el legítimo, para atrapar a unos pocos. La lista es
/// pequeña y cambia muy poco, así que vive en memoria.</para>
///
/// <para>La interfaz está aquí y la implementación en el host, igual que
/// <c>ITelegramMessenger</c>: la capa de aplicación necesita poder AVISAR de que la lista
/// cambió, sin saber nada del pipeline HTTP.</para>
/// </summary>
public interface IBlockedIpCache
{
    /// <summary>¿Está bloqueada ahora mismo?</summary>
    bool EstaBloqueada(string? ipAddress);

    /// <summary>
    /// Relee la lista de la base.
    /// <para>Se llama al bloquear o desbloquear desde el panel para que surta efecto al
    /// instante, en vez de esperar al refresco periódico. Sin esto, bloquear una IP y ver
    /// que sigue entrando durante medio minuto parece que el botón no funciona.</para>
    /// </summary>
    Task RefrescarAsync();
}
