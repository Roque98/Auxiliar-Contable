using System;
using System.Security.Cryptography;
using Volo.Abp.Domain.Entities;

namespace AuxiliarContable.Credenciales;

/// <summary>
/// El NIP de 4 dígitos con que una persona abre las credenciales (la e.firma, la CIEC). Es de la PERSONA, no del
/// cliente: el mismo NIP abre las de todos los clientes que esa persona puede ver. Se guarda solo su huella
/// (PBKDF2-SHA256 con sal); los intentos fallidos se cuentan aquí y también al usar la contraseña de la cuenta.
/// </summary>
public class NipDeUsuario : Entity<Guid>
{
    private const int Iteraciones = 210_000;

    public Guid UsuarioId { get; protected set; }
    public byte[]? Sal { get; protected set; }
    public byte[]? Huella { get; protected set; }
    public int IntentosFallidos { get; protected set; }
    public DateTime? BloqueadoHastaUtc { get; protected set; }

    protected NipDeUsuario()
    {
    }

    public NipDeUsuario(Guid id, Guid usuarioId) : base(id)
    {
        UsuarioId = usuarioId;
    }

    public bool TieneNip => Huella is not null;

    public static bool EsValido(string? nip) => nip is { Length: 4 } && nip.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0;

    public void Poner(string nip)
    {
        if (!EsValido(nip))
        {
            throw new ArgumentException("El NIP son 4 dígitos.");
        }

        Sal = RandomNumberGenerator.GetBytes(16);
        Huella = Calcular(nip, Sal);
        IntentosFallidos = 0;
        BloqueadoHastaUtc = null;
    }

    public bool Bloqueado(DateTime ahoraUtc) => BloqueadoHastaUtc is { } h && h > ahoraUtc;

    public bool Coincide(string nip) =>
        TieneNip && EsValido(nip) && CryptographicOperations.FixedTimeEquals(Calcular(nip, Sal!), Huella);

    public void Fallo(DateTime ahoraUtc)
    {
        IntentosFallidos++;
        if (IntentosFallidos >= CredencialesConsts.MaxIntentos)
        {
            BloqueadoHastaUtc = ahoraUtc.AddMinutes(CredencialesConsts.MinutosDeBloqueo);
            IntentosFallidos = 0;
        }
    }

    public void Acierto() => IntentosFallidos = 0;

    private static byte[] Calcular(string nip, byte[] sal) =>
        Rfc2898DeriveBytes.Pbkdf2(nip, sal, Iteraciones, HashAlgorithmName.SHA256, 32);
}
