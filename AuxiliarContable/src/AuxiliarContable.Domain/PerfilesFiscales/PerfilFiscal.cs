using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.PerfilesFiscales;

/// <summary>
/// El perfil fiscal de un cliente (una organización = un RFC). Un dato por campo del
/// catálogo (<see cref="CamposDelPerfil"/>), cada uno con su origen y si está confirmado.
///
/// <para>La regla que protege al cliente: <b>lo confirmado no lo pisa una extracción</b>.
/// Si una CSF nueva trae otro valor para un dato que ya se confirmó, se devuelve como
/// diferencia para que una persona decida; no se sobrescribe en silencio.</para>
/// </summary>
public class PerfilFiscal : FullAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }

    public ICollection<DatoDelPerfil> Datos { get; protected set; } = [];

    /// <summary>La última CSF que se leyó (o se está leyendo).</summary>
    public Guid? UltimaCsfId { get; protected set; }

    /// <summary>
    /// Lo que hay que señalarle al cliente de esa lectura, una línea por aviso (constancia
    /// antigua, dato distinto a uno confirmado, régimen que no cuadra…). Nunca datos sensibles
    /// sueltos: se enseña en pantalla y el asistente los lee.
    /// </summary>
    public string? AvisosDeLaCsf { get; protected set; }

    public const int MaxAvisosLength = 4000;

    protected PerfilFiscal()
    {
    }

    public PerfilFiscal(Guid id, Guid organizacionId) : base(id)
    {
        OrganizacionId = organizacionId;
    }

    public DatoDelPerfil? Dato(string clave) => Datos.FirstOrDefault(d => d.Campo == clave);

    /// <summary>Se subió una CSF: queda como la «última» mientras se lee.</summary>
    public void EmpezarLecturaDeCsf(Guid documentoId)
    {
        UltimaCsfId = documentoId;
        AvisosDeLaCsf = null;
    }

    public void TerminarLecturaDeCsf(Guid documentoId, IEnumerable<string> avisos)
    {
        // Si mientras tanto se subió otra, los avisos de ESTA ya no son los que importan.
        if (UltimaCsfId != documentoId)
        {
            return;
        }

        var texto = string.Join('\n', avisos.Where(a => !string.IsNullOrWhiteSpace(a)));
        AvisosDeLaCsf = texto.Length == 0 ? null : texto.Length <= MaxAvisosLength ? texto : texto[..MaxAvisosLength];
    }

    /// <summary>
    /// Guarda lo que leyó una extracción (CSF) o dedujo el asistente. Devuelve false si NO lo
    /// guardó porque ya había un valor distinto confirmado: eso es una diferencia a revisar.
    /// </summary>
    public bool Proponer(string clave, string valor, OrigenDelDato origen, Guid? documentoId, DateTime ahoraUtc)
    {
        if (origen is not (OrigenDelDato.ExtraidoDeCsf or OrigenDelDato.Inferido))
        {
            throw new ArgumentException("Proponer es para lo extraído o inferido; lo que dice una persona va por Responder.", nameof(origen));
        }

        var existente = Dato(ValidarClave(clave));
        if (existente is { Confirmado: true })
        {
            return existente.Valor == valor;
        }

        Escribir(clave, valor, origen, confirmado: false, documentoId, ahoraUtc);
        return true;
    }

    /// <summary>
    /// Lo que dice una persona (el cliente o el despacho): queda confirmado, porque lo afirma
    /// quien sabe. Sustituye cualquier valor anterior.
    /// </summary>
    public void Responder(string clave, string valor, OrigenDelDato origen, DateTime ahoraUtc)
    {
        if (origen is not (OrigenDelDato.RespondidoPorElCliente or OrigenDelDato.CapturadoPorElDespacho))
        {
            throw new ArgumentException("Responder es para lo que dice una persona.", nameof(origen));
        }

        Escribir(ValidarClave(clave), valor, origen, confirmado: true, documentoId: null, ahoraUtc);
    }

    /// <summary>Da por bueno un dato extraído o inferido tal como está (el cliente dijo «sí, es correcto»).</summary>
    public void Confirmar(string clave, DateTime ahoraUtc)
    {
        var dato = Dato(ValidarClave(clave)) ?? throw new BusinessException("AuxiliarContable:DatoDelPerfilNoExiste");
        dato.Confirmar(ahoraUtc);
    }

    /// <summary>
    /// «Todo lo de la constancia está bien»: confirma de una vez lo extraído de la CSF que
    /// sigue sin confirmar. Lo inferido NO: eso lo dedujo alguien y se revisa uno por uno.
    /// </summary>
    public int ConfirmarLoDeLaConstancia(DateTime ahoraUtc)
    {
        var pendientes = Datos.Where(d => d is { Confirmado: false, Origen: OrigenDelDato.ExtraidoDeCsf }).ToList();
        foreach (var dato in pendientes)
        {
            dato.Confirmar(ahoraUtc);
        }

        return pendientes.Count;
    }

    private void Escribir(string clave, string valor, OrigenDelDato origen, bool confirmado, Guid? documentoId, DateTime ahoraUtc)
    {
        var limpio = Check.NotNullOrWhiteSpace(valor, nameof(valor), CamposDelPerfil.MaxValorLength).Trim();
        var dato = Dato(clave);
        if (dato == null)
        {
            Datos.Add(new DatoDelPerfil(Id, clave, limpio, origen, confirmado, documentoId, ahoraUtc));
        }
        else
        {
            dato.Cambiar(limpio, origen, confirmado, documentoId, ahoraUtc);
        }
    }

    private static string ValidarClave(string clave) =>
        CamposDelPerfil.Buscar(clave) != null
            ? clave
            : throw new BusinessException("AuxiliarContable:CampoDelPerfilDesconocido").WithData("campo", clave);
}

/// <summary>Un dato del perfil con su procedencia.</summary>
public class DatoDelPerfil : Entity
{
    public Guid PerfilFiscalId { get; protected set; }

    /// <summary>Clave de <see cref="CamposDelPerfil"/>.</summary>
    public string Campo { get; protected set; } = string.Empty;

    /// <summary>Texto, fecha ISO (AAAA-MM-DD), «si»/«no», o JSON para las listas.</summary>
    public string Valor { get; protected set; } = string.Empty;

    public OrigenDelDato Origen { get; protected set; }

    public bool Confirmado { get; protected set; }

    /// <summary>El documento del que salió (la CSF), para enseñarlo junto al dato.</summary>
    public Guid? DocumentoId { get; protected set; }

    public DateTime ActualizadoUtc { get; protected set; }

    protected DatoDelPerfil()
    {
    }

    internal DatoDelPerfil(Guid perfilFiscalId, string campo, string valor, OrigenDelDato origen, bool confirmado, Guid? documentoId, DateTime ahoraUtc)
    {
        PerfilFiscalId = perfilFiscalId;
        Campo = campo;
        Cambiar(valor, origen, confirmado, documentoId, ahoraUtc);
    }

    internal void Cambiar(string valor, OrigenDelDato origen, bool confirmado, Guid? documentoId, DateTime ahoraUtc)
    {
        Valor = valor;
        Origen = origen;
        Confirmado = confirmado;
        DocumentoId = documentoId;
        ActualizadoUtc = ahoraUtc;
    }

    internal void Confirmar(DateTime ahoraUtc)
    {
        Confirmado = true;
        ActualizadoUtc = ahoraUtc;
    }

    public override object[] GetKeys() => [PerfilFiscalId, Campo];
}
