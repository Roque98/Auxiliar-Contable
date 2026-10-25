using AuxiliarContable.Antivirus;
using AuxiliarContable.Documentos;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

public class ClamAvEscanerTests
{
    [Fact]
    public void Limpio()
    {
        ClamAvEscaner.Interpretar("stream: OK\0").Veredicto.ShouldBe(VeredictoDelEscaneo.Limpio);
    }

    [Fact]
    public void Infectado_con_su_firma()
    {
        var r = ClamAvEscaner.Interpretar("stream: Eicar-Signature FOUND\0");
        r.Veredicto.ShouldBe(VeredictoDelEscaneo.Infectado);
        r.Detalle.ShouldBe("Eicar-Signature");
    }

    [Fact]
    public void Un_error_de_clamd_no_se_toma_por_limpio()
    {
        ClamAvEscaner.Interpretar("INSTREAM size limit exceeded. ERROR\0").Veredicto.ShouldBe(VeredictoDelEscaneo.NoDisponible);
    }
}
