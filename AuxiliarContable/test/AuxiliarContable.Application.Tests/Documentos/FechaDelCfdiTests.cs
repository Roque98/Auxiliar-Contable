using System;
using System.Text;
using Shouldly;
using Xunit;

namespace AuxiliarContable.Documentos;

/// <summary>La fecha de emisión de un CFDI, para acomodarlo en su mes.</summary>
public class FechaDelCfdiTests
{
    private static byte[] Xml(string texto) => Encoding.UTF8.GetBytes(texto);

    [Fact]
    public void Lee_la_fecha_del_comprobante()
    {
        var xml = Xml("""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" Fecha="2026-10-31T23:59:00"><cfdi:Emisor Rfc="X"/></cfdi:Comprobante>""");

        FechaDelCfdi.Leer(xml).ShouldBe(new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void Se_guarda_a_mediodia_UTC_para_no_cambiar_de_dia()
    {
        var fecha = FechaDelCfdi.ComoFecha(new DateOnly(2026, 10, 31));

        fecha.ShouldBe(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc));
        fecha.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("""<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0"/>""")]
    [InlineData("""<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Fecha="mañana"/>""")]
    [InlineData("""<otra><cosa Fecha="2026-10-01T10:00:00"/></otra>""")]
    [InlineData("no es xml")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Fecha="2026-10-01T10:00:00"/>""")]
    public void Sin_fecha_valida_no_hay_fecha(string texto)
    {
        FechaDelCfdi.Leer(Xml(texto)).ShouldBeNull();
    }
}
