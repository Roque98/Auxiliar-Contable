using System;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.Documentos;

/// <summary>Contenedor de BLOBs de los documentos. En el host va al disco (CLAUDE.md §1.2.10).</summary>
[BlobContainerName(DocumentoConsts.ContainerName)]
public class DocumentosContainer
{
}

/// <summary>
/// Guarda y lee el CONTENIDO de los documentos, siempre cifrado. Nadie más toca el contenedor:
/// así es imposible guardar un documento en claro por descuido.
/// </summary>
public class AlmacenDeDocumentos : ITransientDependency
{
    private readonly IBlobContainer<DocumentosContainer> _contenedor;
    private readonly CifradorDeDocumentos _cifrador;

    public AlmacenDeDocumentos(IBlobContainer<DocumentosContainer> contenedor, CifradorDeDocumentos cifrador)
    {
        _contenedor = contenedor;
        _cifrador = cifrador;
    }

    public Task GuardarAsync(Documento documento, byte[] contenido) =>
        _contenedor.SaveAsync(Nombre(documento), _cifrador.Cifrar(documento.Id, contenido), overrideExisting: true);

    public async Task<byte[]> LeerAsync(Documento documento)
    {
        var cifrado = await _contenedor.GetAllBytesOrNullAsync(Nombre(documento))
                      ?? throw new AbpException($"Falta el contenido del documento {documento.Id}.");
        return _cifrador.Descifrar(documento.Id, cifrado);
    }

    public Task BorrarAsync(Documento documento) => _contenedor.DeleteAsync(Nombre(documento));

    /// <summary>Una carpeta por cliente: borrar o respaldar lo de uno no toca lo de otro.</summary>
    private static string Nombre(Documento documento) => $"{documento.OrganizacionId:N}/{documento.Id:N}";
}
