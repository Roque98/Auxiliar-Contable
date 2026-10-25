using Volo.Abp.BlobStoring;

namespace AuxiliarContable.Branding;

/// <summary>
/// Contenedor tipado donde vive el logo de cada inquilino. La clase está vacía a propósito:
/// para el sistema de BLOBs de ABP es solo una etiqueta que permite inyectar
/// <c>IBlobContainer&lt;TenantLogoContainer&gt;</c> y configurar ESTE contenedor aparte de
/// los demás.
///
/// <para>No hace falta declararlo multi-tenant: <c>BlobContainerConfiguration.IsMultiTenant</c>
/// vale <c>true</c> de serie y el contenedor guarda cada archivo bajo
/// <c>ICurrentTenant.Id</c>. Para leer el logo de OTRO inquilino —lo que hace el endpoint
/// público— hay que envolver la lectura en <c>ICurrentTenant.Change(id)</c>.</para>
/// </summary>
[BlobContainerName(BrandingConsts.ContainerName)]
public class TenantLogoContainer
{
}
