using System.Text;
using AuxiliarContable.Cfdis;
using Shouldly;
using Xunit;

namespace AuxiliarContable.Cfdis;

public class LectorDeCfdiTests
{
    /// <summary>Un CFDI 4.0 mínimo con la forma real (namespaces, timbre en el complemento).</summary>
    public static string Cfdi40(
        string uuid = "6F3E8B1A-1234-4C5D-9E8F-0A1B2C3D4E5F",
        string emisor = "GODE561231GR8",
        string receptor = "XAXX010101000",
        string total = "1160.00",
        string tipo = "I",
        string fecha = "2026-09-15T10:30:00") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital"
            Version="4.0" Serie="A" Folio="123" Fecha="{fecha}" SubTotal="1000.00" Total="{total}" Moneda="MXN"
            TipoDeComprobante="{tipo}" MetodoPago="PUE" FormaPago="03" Exportacion="01" LugarExpedicion="06600">
          <cfdi:Emisor Rfc="{emisor}" Nombre="EDUARDO GOMEZ DIAZ" RegimenFiscal="626"/>
          <cfdi:Receptor Rfc="{receptor}" Nombre="PUBLICO EN GENERAL" UsoCFDI="S01" DomicilioFiscalReceptor="06600" RegimenFiscalReceptor="616"/>
          <cfdi:Conceptos>
            <cfdi:Concepto ClaveProdServ="50181900" Cantidad="10" ClaveUnidad="H87" Descripcion="Pan dulce" ValorUnitario="100.00" Importe="1000.00" ObjetoImp="02"/>
          </cfdi:Conceptos>
          <cfdi:Impuestos TotalImpuestosTrasladados="160.00"/>
          <cfdi:Complemento>
            <tfd:TimbreFiscalDigital Version="1.1" UUID="{uuid}" FechaTimbrado="2026-09-15T10:31:00" RfcProvCertif="SAT970701NN3" SelloCFD="x" NoCertificadoSAT="1" SelloSAT="y"/>
          </cfdi:Complemento>
        </cfdi:Comprobante>
        """;

    [Fact]
    public void Lee_los_datos_de_un_CFDI_40()
    {
        var d = LectorDeCfdi.Leer(Encoding.UTF8.GetBytes(Cfdi40(uuid: "6f3e8b1a-1234-4c5d-9e8f-0a1b2c3d4e5f")));

        d.Version.ShouldBe("4.0");
        d.Uuid.ShouldBe("6F3E8B1A-1234-4C5D-9E8F-0A1B2C3D4E5F");
        d.Fecha.ShouldBe(new System.DateTime(2026, 9, 15, 10, 30, 0));
        d.TipoDeComprobante.ShouldBe("I");
        d.EmisorRfc.ShouldBe("GODE561231GR8");
        d.ReceptorRfc.ShouldBe("XAXX010101000");
        d.SubTotal.ShouldBe(1000m);
        d.Total.ShouldBe(1160m);
        d.TotalTrasladados.ShouldBe(160m);
        d.MetodoDePago.ShouldBe("PUE");
        d.Conceptos.ShouldBe(1);
        d.PrimerConcepto.ShouldBe("Pan dulce");
    }

    [Fact]
    public void Lee_tambien_la_version_33()
    {
        var xml = Cfdi40().Replace("http://www.sat.gob.mx/cfd/4", "http://www.sat.gob.mx/cfd/3").Replace("Version=\"4.0\"", "Version=\"3.3\"");
        LectorDeCfdi.Leer(Encoding.UTF8.GetBytes(xml)).Version.ShouldBe("3.3");
    }

    [Fact]
    public void Rechaza_un_XML_que_no_es_CFDI()
    {
        Should.Throw<CfdiInvalidoException>(() => LectorDeCfdi.Leer("<factura><total>5</total></factura>"u8.ToArray()))
            .Message.ShouldContain("no es un CFDI");
    }

    [Fact]
    public void Rechaza_un_CFDI_sin_timbre()
    {
        var sinTimbre = Cfdi40().Replace("UUID=\"6F3E8B1A-1234-4C5D-9E8F-0A1B2C3D4E5F\"", string.Empty);
        Should.Throw<CfdiInvalidoException>(() => LectorDeCfdi.Leer(Encoding.UTF8.GetBytes(sinTimbre)))
            .Message.ShouldContain("timbrado");
    }

    [Fact]
    public void Rechaza_DTD_y_entidades_externas()
    {
        var xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE c [<!ENTITY x SYSTEM "file:///etc/passwd">]>
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Total="&x;"/>
            """;
        Should.Throw<CfdiInvalidoException>(() => LectorDeCfdi.Leer(Encoding.UTF8.GetBytes(xxe)))
            .Message.ShouldContain("XML válido");
    }

    [Fact]
    public void Un_importe_que_no_es_numero_no_se_inventa()
    {
        Should.Throw<CfdiInvalidoException>(() => LectorDeCfdi.Leer(Encoding.UTF8.GetBytes(Cfdi40(total: "mil"))))
            .Message.ShouldContain("Total");
    }

    [Theory]
    [InlineData("GODE561231GR8", DireccionDelCfdi.Emitido)]
    [InlineData("XAXX010101000", DireccionDelCfdi.Recibido)]
    [InlineData("OTRO010101AAA", DireccionDelCfdi.Desconocida)]
    [InlineData(null, DireccionDelCfdi.Desconocida)]
    public void La_direccion_sale_del_RFC_del_cliente(string? rfc, DireccionDelCfdi esperada)
    {
        Cfdi.DireccionPara("GODE561231GR8", "XAXX010101000", rfc).ShouldBe(esperada);
    }
}
