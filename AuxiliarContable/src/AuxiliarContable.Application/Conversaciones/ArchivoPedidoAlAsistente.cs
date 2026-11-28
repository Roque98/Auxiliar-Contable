using AuxiliarContable.Exportaciones;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// Lo que pidió el asistente con generar_archivo. No se guarda el archivo: se vuelve a armar del pedido cada vez que se
/// descarga, igual que como se armó al pedirlo (sin IA, siempre igual).
/// </summary>
public sealed record ArchivoPedidoAlAsistente(FormatoDeArchivo Formato, string? Nombre, string Contenido, CalculoDeArchivo? Calculo, string? Formal)
{
    public ArchivoGenerado Generar() => ArchivoDesdeTexto.Generar(Formato, Nombre, Contenido, Calculo, Formal);

    public string NombreDelArchivo => ArchivoDesdeTexto.NombrePara(Formato, Nombre, Contenido);
}
