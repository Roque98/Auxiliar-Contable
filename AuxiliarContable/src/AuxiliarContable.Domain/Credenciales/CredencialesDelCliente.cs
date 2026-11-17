using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Credenciales;

/// <summary>
/// La e.firma y la CIEC de un cliente (una fila por organización). Lo secreto va cifrado con
/// <see cref="CifradorDeCredenciales"/> (los campos <c>*Cifrado</c>); lo que sirve para avisar sin descifrar nada (RFC,
/// número de certificado, vigencia) va en claro.
///
/// <para>Se guardan porque el cliente las entrega al contratar el servicio: el despacho las necesita para descargar sus
/// facturas, entrar al portal y presentar trámites. Sin consentimiento registrado no se guardan
/// (<see cref="RegistrarConsentimiento"/>).</para>
/// </summary>
public class CredencialesDelCliente : AuditedAggregateRoot<Guid>
{
    public const string CampoCer = "efirma.cer";
    public const string CampoKey = "efirma.key";
    public const string CampoContrasenaDeEfirma = "efirma.contrasena";
    public const string CampoCiec = "ciec.contrasena";

    public Guid OrganizacionId { get; protected set; }

    public DateTime? ConsentimientoUtc { get; protected set; }
    public Guid? ConsentimientoPor { get; protected set; }

    public byte[]? CerCifrado { get; protected set; }
    public byte[]? KeyCifrado { get; protected set; }
    public byte[]? ContrasenaDeEfirmaCifrada { get; protected set; }
    public string? EfirmaRfc { get; protected set; }
    public string? NumeroDeCertificado { get; protected set; }
    public DateTime? VigenteDesdeUtc { get; protected set; }
    public DateTime? VigenteHastaUtc { get; protected set; }
    public DateTime? EfirmaActualizadaUtc { get; protected set; }

    public string? CiecRfc { get; protected set; }
    public byte[]? CiecCifrada { get; protected set; }
    public DateTime? CiecActualizadaUtc { get; protected set; }

    protected CredencialesDelCliente()
    {
    }

    public CredencialesDelCliente(Guid id, Guid organizacionId) : base(id)
    {
        OrganizacionId = organizacionId;
    }

    public bool TieneEfirma => CerCifrado is not null;
    public bool TieneCiec => CiecCifrada is not null;

    public void RegistrarConsentimiento(Guid? usuarioId, DateTime ahoraUtc)
    {
        Utc(ahoraUtc);
        ConsentimientoUtc ??= ahoraUtc;
        ConsentimientoPor ??= usuarioId;
    }

    public void PonerEfirma(byte[] cer, byte[] key, byte[] contrasena, DatosDeEfirma datos, DateTime ahoraUtc)
    {
        ExigirConsentimiento();
        Utc(ahoraUtc);
        CerCifrado = cer;
        KeyCifrado = key;
        ContrasenaDeEfirmaCifrada = contrasena;
        EfirmaRfc = datos.Rfc;
        NumeroDeCertificado = datos.NumeroDeCertificado;
        VigenteDesdeUtc = datos.VigenteDesdeUtc;
        VigenteHastaUtc = datos.VigenteHastaUtc;
        EfirmaActualizadaUtc = ahoraUtc;
    }

    public void PonerCiec(string rfc, byte[] contrasena, DateTime ahoraUtc)
    {
        ExigirConsentimiento();
        Utc(ahoraUtc);
        CiecRfc = rfc;
        CiecCifrada = contrasena;
        CiecActualizadaUtc = ahoraUtc;
    }

    public void Borrar(TipoDeCredencial tipo)
    {
        if (tipo == TipoDeCredencial.Efirma)
        {
            CerCifrado = KeyCifrado = ContrasenaDeEfirmaCifrada = null;
            EfirmaRfc = NumeroDeCertificado = null;
            VigenteDesdeUtc = VigenteHastaUtc = EfirmaActualizadaUtc = null;
        }
        else
        {
            CiecRfc = null;
            CiecCifrada = null;
            CiecActualizadaUtc = null;
        }
    }

    private void ExigirConsentimiento()
    {
        if (ConsentimientoUtc is null)
        {
            throw new BusinessException("AuxiliarContable:CredencialesSinConsentimiento");
        }
    }

    private static void Utc(DateTime d)
    {
        if (d.Kind != DateTimeKind.Utc)
        {
            throw new AbpException("Las fechas de credenciales deben ser UTC (columna timestamptz).");
        }
    }
}
