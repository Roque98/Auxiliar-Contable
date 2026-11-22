namespace AuxiliarContable.LeyesDelCliente;

/// <summary>Una ley del estado o del municipio.</summary>
public enum AmbitoDeLey
{
    Estatal = 0,
    Municipal = 1,
}

/// <summary>En qué va la lectura de la ley en el motor (texto y OCR por artículos, sin Claude).</summary>
public enum LecturaDeLey
{
    Pendiente = 0,
    Leyendo = 1,
    Lista = 2,
    Error = 3,
}

/// <summary>Lo que el despacho decidió de un hallazgo.</summary>
public enum DecisionDeHallazgo
{
    Propuesto = 0,

    /// <summary>Cuenta: un valor con clave reemplaza al del sistema solo para este cliente.</summary>
    Aceptado = 1,

    Rechazado = 2,

    /// <summary>Estaba aceptado y otro hallazgo con la misma clave tomó su lugar.</summary>
    Reemplazado = 3,
}

public enum EstadoDePregunta
{
    Pendiente = 0,
    Lista = 1,
    Error = 2,
}

public static class LeyesDelClienteConsts
{
    public const int MaxEstadoLength = 6;
    public const int MaxMunicipioLength = 120;
    public const int MaxMateriaLength = 200;
    public const int MaxTituloLength = 300;
    public const int MaxResumenLength = 2000;
    public const int MaxErrorLength = 500;
    public const int MaxHuellaLength = 64;
    public const int MaxPreguntaLength = 2000;

    /// <summary>Claves de los valores que pueden reemplazar a los del sistema (las mismas que devuelve el motor).</summary>
    public const string IsnTasa = "isn.tasa";
    public const string IsnDia = "isn.dia";
    public const string RecargosTasa = "recargos.tasa";

    /// <summary>
    /// Lo que se le dice a quien entra al apartado (pedido del 22-nov-2026, palabra por palabra en lo esencial).
    /// </summary>
    public const string Mensaje =
        "Si el sistema no calcula de acuerdo a tu ley estatal o municipal, sube las leyes en este apartado y se trabajará a partir de ahí.";
}
