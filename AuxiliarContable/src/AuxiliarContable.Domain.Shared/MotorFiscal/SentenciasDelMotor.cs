using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

// La librería de sentencias del motor fiscal (sentencias/ en motores-fiscales): tesis, precedentes y
// jurisprudencias del TFJA y de la SCJN, cada una con lo que Claude concluyó de ella. Como el resto de
// lo que devuelve el motor, la consola no lo reinterpreta ni lo guarda: lo muestra y el chat lo cita.

/// <summary>Una tesis tal como la publicó la autoridad, más su estudio (null si aún no se estudia).</summary>
public class SentenciaDelMotor
{
    /// <summary>«tfja:48314» o «scjn:2032328».</summary>
    public string Id { get; set; } = "";

    /// <summary>TFJA o SCJN.</summary>
    public string Fuente { get; set; } = "";

    /// <summary>La clave de la tesis: «IX-J-2aS-115», «2a./J. 12/2026 (12a.)».</summary>
    public string? Clave { get; set; }

    /// <summary>jurisprudencia (obliga), precedente o aislada (orienta).</summary>
    public string? Tipo { get; set; }

    public string? Instancia { get; set; }
    public string? Materia { get; set; }
    public string Rubro { get; set; } = "";

    /// <summary>Dónde se publicó (revista del TFJA, Semanario Judicial).</summary>
    public string? Publicacion { get; set; }

    /// <summary>AAAA-MM-DD de publicación.</summary>
    public string? Fecha { get; set; }

    /// <summary>La tesis en el sitio oficial.</summary>
    public string? Url { get; set; }

    public string Texto { get; set; } = "";
    public EstudioDeSentencia? Estudio { get; set; }
}

/// <summary>Lo que se concluyó de una tesis (motor: sentencias/estudio.py).</summary>
public class EstudioDeSentencia
{
    public string Conclusion { get; set; } = "";

    /// <summary>contribuyente, autoridad, mixto o procesal.</summary>
    public string AFavor { get; set; } = "";

    /// <summary>Qué debe hacer, cuidar o evitar el despacho por este criterio.</summary>
    public string ReglaPractica { get; set; } = "";

    /// <summary>La situación de un cliente en la que el criterio importa.</summary>
    public string AplicaCuando { get; set; } = "";

    public List<string> QueDocumentar { get; set; } = [];

    /// <summary>Solo los artículos que la tesis cita («CFF art. 28 fr. I»).</summary>
    public List<string> Fundamentos { get; set; } = [];

    public List<string> Temas { get; set; } = [];

    /// <summary>Si descansa en una ley de un ejercicio que pudo cambiar, qué revisar.</summary>
    public string Vigencia { get; set; } = "";

    /// <summary>alta, media o baja para un despacho de personas físicas y pymes.</summary>
    public string Relevancia { get; set; } = "";
}

/// <summary>Cuánto hay en la librería y cómo va la actualización (si hay una).</summary>
public class EstadoDeSentencias
{
    public LibreriaDeSentencias Libreria { get; set; } = new();
    public TrabajoDeSentencias Trabajo { get; set; } = new();
}

public class LibreriaDeSentencias
{
    public int Tesis { get; set; }
    public Dictionary<string, int> PorFuente { get; set; } = [];
    public int Estudiadas { get; set; }

    /// <summary>La tesis más reciente que hay (AAAA-MM-DD).</summary>
    public string? UltimaPublicacion { get; set; }

    /// <summary>Cuándo se revisaron por última vez las fuentes (hora del servidor del motor).</summary>
    public string? UltimaActualizacion { get; set; }
}

public class TrabajoDeSentencias
{
    public bool Corriendo { get; set; }

    /// <summary>descargando o estudiando.</summary>
    public string? Fase { get; set; }

    public string? Inicio { get; set; }
    public string? Fin { get; set; }
    public string? Error { get; set; }

    /// <summary>Las últimas líneas de lo que va haciendo («TFJA «materialidad»: 3 nuevas»).</summary>
    public List<string> Bitacora { get; set; } = [];
}
