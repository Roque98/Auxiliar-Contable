using System;
using System.Text;
using Shouldly;
using Xunit;

namespace AuxiliarContable.Documentos;

/// <summary>Un CFDI de Retenciones 2.0 (raíz retenciones:Retenciones) ya no queda como «XML dañado».</summary>
public class RetencionesTests
{
    private const string Uuid = "AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE";

    private static byte[] Xml(int mesIni, int mesFin, int ejercicio = 2026) => Encoding.UTF8.GetBytes(
        $"""
        <?xml version="1.0" encoding="UTF-8"?><retenciones:Retenciones xmlns:retenciones="http://www.sat.gob.mx/esquemas/retencionpago/2"
         Version="2.0" FechaExp="2027-02-10T10:00:00" CveRetenc="16" DescRetenc="Intereses"><retenciones:Emisor RfcE="BAN010101AAA"
         NomDenRazSocE="BANCO" RegimenFiscalE="601"/><retenciones:Receptor NacionalidadR="Nacional"><retenciones:Nacional RfcR="peje800101ab1"
         NomDenRazSocR="CLIENTE" DomicilioFiscalR="62420"/></retenciones:Receptor><retenciones:Periodo MesIni="{mesIni}" MesFin="{mesFin}"
         Ejercicio="{ejercicio}"/><retenciones:Totales MontoTotOperacion="5000.00" MontoTotGrav="5000" MontoTotExent="0" MontoTotRet="450"/>
         <retenciones:Complemento><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" UUID="{Uuid.ToLowerInvariant()}"/>
         </retenciones:Complemento></retenciones:Retenciones>
        """.Trim());

    [Fact]
    public void Se_lee_su_uuid_emisor_receptor_y_monto()
    {
        var d = DatosDelCfdi.Leer(Xml(1, 12)).ShouldNotBeNull();
        d.EsRetencion.ShouldBeTrue();
        (d.Uuid, d.EmisorRfc, d.ReceptorRfc, d.Total).ShouldBe((Uuid, "BAN010101AAA", "PEJE800101AB1", (decimal?)5000m));
        d.PrimerConcepto.ShouldBe("Retenciones: Intereses");
    }

    [Fact]
    public void Se_fecha_en_el_ultimo_dia_de_su_periodo_no_cuando_se_expidio()
    {
        // La constancia anual de intereses de 2026 llega en febrero de 2027: es de diciembre de 2026.
        FechaDelCfdi.Leer(Xml(1, 12)).ShouldBe(new DateOnly(2026, 12, 31));
        FechaDelCfdi.Leer(Xml(2, 2)).ShouldBe(new DateOnly(2026, 2, 28));
        DatosDelCfdi.Leer(Xml(3, 3))!.Fecha.ShouldBe(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void Sin_periodo_valido_no_hay_fecha_y_el_total_de_factura_no_aplica()
    {
        FechaDelCfdi.Leer(Xml(1, 13)).ShouldBeNull();
        DatosDelCfdi.TotalTalCual(Xml(1, 12)).ShouldBeNull();
    }
}
