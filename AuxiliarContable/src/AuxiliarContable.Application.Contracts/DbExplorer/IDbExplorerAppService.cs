using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.DbExplorer;

/// <summary>
/// Explorador de BD sobre la base principal. HERRAMIENTA DE ADMINISTRACIÓN: solo
/// disponible en Development, protegida por permiso. La lectura abarca todas las
/// tablas; la edición solo las tablas de la app (prefijo App). El CRUD genérico
/// salta la validación de dominio y la auditoría de ABP.
/// </summary>
public interface IDbExplorerAppService : IApplicationService
{
    Task<List<TableInfoDto>> GetTablesAsync();

    Task<TableSchemaDto> GetSchemaAsync(string schema, string table);

    Task<TableRowsDto> GetRowsAsync(GetRowsInput input);

    Task InsertAsync(InsertRowInput input);

    Task UpdateAsync(UpdateRowInput input);

    Task DeleteAsync(DeleteRowInput input);
}
