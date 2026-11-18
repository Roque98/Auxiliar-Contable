using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// El cliente no entregó nada: el despacho marca el mes, baja la orden para el Descargador 1, sube el paquete que deja el
/// robot (solo entra lo que faltaba, los dañados se cambian por el bueno, cada factura con su estatus del SAT) y calcula
/// el pago provisional solo con eso (las canceladas no cuentan). El cliente no entra.
/// </summary>
public class DescargaDelSatIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";
    private const string Regimen = "Régimen de las Personas Físicas con Actividades Empresariales y Profesionales";
    private const string Compra = "6F9619FF-8B86-D011-B42D-00C04FC964F1";
    private const string Venta = "6F9619FF-8B86-D011-B42D-00C04FC964F2";
    private const string Ajena = "6F9619FF-8B86-D011-B42D-00C04FC964F3";
    private const string Danada = "6F9619FF-8B86-D011-B42D-00C04FC964F4";

    private readonly IDescargaDelSatAppService _descarga;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    private static DateTime Dia => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    public DescargaDelSatIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _descarga = GetRequiredService<IDescargaDelSatAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Carpintería Lupita");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        var perfil = GetRequiredService<IPerfilFiscalAppService>();
        await ComoAsync(Administrador, () => perfil.ResponderAsync(_cliente, new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = Rfc }));
        await ComoAsync(Administrador, () => perfil.ResponderAsync(_cliente, new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Regimenes, Valor = JsonSerializer.Serialize(new[] { Regimen }) }));
    }

    private static string Xml(string uuid, string emisor, string receptor, decimal total) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{Dia:yyyy-MM-dd}T10:00:00" Total="{total:0.00}" TipoDeComprobante="I"><cfdi:Emisor Rfc="{emisor}" Nombre="EMISOR"/><cfdi:Receptor Rfc="{receptor}" Nombre="RECEPTOR"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Madera"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>""";

    private static RemoteStreamContent Paquete(string rfc, Dictionary<string, string> archivos, string estatus, int noDisponibles = 0)
    {
        var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Pon(string nombre, string texto)
            {
                using var e = zip.CreateEntry(nombre).Open();
                e.Write(Encoding.UTF8.GetBytes(texto));
            }

            Pon("resumen.json", JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["tipo"] = "paquete-descargador-1", ["rfc"] = rfc, ["desde"] = $"{Dia:yyyy-MM}", ["hasta"] = $"{Dia:yyyy-MM}",
                ["danados"] = Array.Empty<string>(), ["no_disponibles"] = Enumerable.Range(0, noDisponibles).Select(i => $"X{i}").ToArray(),
            }));
            Pon("estatus.csv", "﻿uuid,consulta,estado,estatus_cancelacion,fecha_cancelacion,rfc_emisor,rfc_receptor,fecha_emision,total\n" + estatus);
            foreach (var (nombre, texto) in archivos)
            {
                Pon(nombre, texto);
            }
        }

        memoria.Position = 0;
        return new RemoteStreamContent(memoria, "paquete.zip", "application/zip");
    }

    [Fact]
    public async Task El_cliente_no_entrego_nada_se_baja_todo_del_sat_y_se_calcula_el_impuesto()
    {
        // El cliente no entra.
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _descarga.GetAsync(_cliente, Dia.Year, Dia.Month)));

        await ComoAsync(Administrador, () => _descarga.MarcarSinEntregaAsync(_cliente, Dia.Year, Dia.Month, true));
        var estado = await ComoAsync(Administrador, () => _descarga.GetAsync(_cliente, Dia.Year, Dia.Month));
        estado.SinEntregaDelCliente.ShouldBeTrue();
        estado.TieneRfc.ShouldBeTrue();
        estado.Meses.Count.ShouldBe(Dia.Month);
        estado.Meses.Last().Facturas.ShouldBe(0);

        // Sin nada aquí, la orden va sin «tengo»: el robot baja todo.
        var orden = await ComoAsync(Administrador, () => _descarga.GetOrdenAsync(_cliente, Dia.Year, Dia.Month, Dia.Year, Dia.Month, true, true));
        orden.Tengo.ShouldBe(0);
        orden.NombreDeArchivo.ShouldEndWith(".json");
        using (var json = JsonDocument.Parse(orden.Contenido))
        {
            json.RootElement.GetProperty("tipo").GetString().ShouldBe("orden-descargador-1");
            json.RootElement.GetProperty("rfc").GetString().ShouldBe(Rfc);
            json.RootElement.GetProperty("tipos").GetArrayLength().ShouldBe(2);
            json.RootElement.GetProperty("tengo").GetArrayLength().ShouldBe(0);
        }

        // El paquete: una compra y una venta del cliente; una factura de otro RFC y un archivo que no abre se rechazan.
        var archivos = new Dictionary<string, string>
        {
            [$"xml/recibidas/{Compra}.xml"] = Xml(Compra, "AAA010101AAA", Rfc, 1160m),
            [$"xml/emitidas/{Venta}.xml"] = Xml(Venta, Rfc, "XAXX010101000", 5800m),
            [$"xml/recibidas/{Ajena}.xml"] = Xml(Ajena, "AAA010101AAA", "BBB010101BBB", 100m),
            ["xml/recibidas/roto.xml"] = "<html>Error del SAT</html>",
        };
        var estatus = $"{Compra},recibidas,Vigente,,,AAA010101AAA,{Rfc},,\n{Venta},emitidas,Cancelado,Cancelado sin aceptación,,{Rfc},XAXX010101000,,\"$5,800.00\"\n";
        var r = await ComoAsync(Administrador, () => _descarga.SubirPaqueteAsync(_cliente, Paquete(Rfc, archivos, estatus, noDisponibles: 1)));
        r.Nuevas.ShouldBe(2);
        r.Rechazadas.ShouldBe(2);
        r.EstatusPuestos.ShouldBe(2);
        r.Canceladas.ShouldBe(1);
        r.SinXmlEnElSat.ShouldBe(1);

        // Otra vez el mismo paquete: nada se duplica.
        var otra = await ComoAsync(Administrador, () => _descarga.SubirPaqueteAsync(_cliente, Paquete(Rfc, archivos, estatus)));
        otra.Nuevas.ShouldBe(0);
        otra.Repetidas.ShouldBe(2);

        estado = await ComoAsync(Administrador, () => _descarga.GetAsync(_cliente, Dia.Year, Dia.Month));
        var mes = estado.Meses.Last();
        mes.Facturas.ShouldBe(2);
        mes.Canceladas.ShouldBe(1);
        mes.SinEstatus.ShouldBe(0);
        mes.SinEntregaDelCliente.ShouldBeTrue();
        estado.UltimoEstatusUtc.ShouldNotBeNull();

        // La siguiente orden ya dice qué hay: el robot solo baja lo demás.
        (await ComoAsync(Administrador, () => _descarga.GetOrdenAsync(_cliente, Dia.Year, Dia.Month, Dia.Year, Dia.Month, true, true))).Tengo.ShouldBe(2);

        // El impuesto: las dos facturas al motor, la cancelada marcada, y el régimen del perfil.
        var impuestos = await ComoAsync(Administrador, () => _descarga.CalcularImpuestosAsync(_cliente, Dia.Year, Dia.Month));
        impuestos.SinEntregaDelCliente.ShouldBeTrue();
        impuestos.FacturasEnviadas.ShouldBe(2);
        impuestos.Canceladas.ShouldBe(1);
        impuestos.Resultado.Archivo.ShouldNotBeNull();
        var enviado = _motor.Provisionales.ShouldNotBeNull();
        enviado.Rfc.ShouldBe(Rfc);
        enviado.Periodo.ShouldBe($"{Dia:yyyy-MM}");
        enviado.Regimenes.ShouldBe([Regimen]);
        enviado.Cancelados.ShouldBe([Venta]);
        enviado.Facturas.Order().ShouldBe(new[] { $"{Compra}.xml", $"{Venta}.xml" }.Order());

        // La DIOT: las mismas facturas al motor, la cancelada marcada; el cliente no la ve.
        var diot = await ComoAsync(Administrador, () => _descarga.GetDiotAsync(_cliente, Dia.Year, Dia.Month));
        diot.Resultado.Renglones.ShouldHaveSingleItem().IvaAcreditable.ShouldBe(160);
        diot.Resultado.Archivo.ShouldNotBeNull();
        var paraDiot = _motor.DiotRecibida.ShouldNotBeNull();
        paraDiot.Periodo.ShouldBe($"{Dia:yyyy-MM}");
        paraDiot.Cancelados.ShouldBe([Venta]);
        paraDiot.Regimenes.ShouldBe([Regimen]);
        paraDiot.Facturas.Count.ShouldBe(2);
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _descarga.GetDiotAsync(_cliente, Dia.Year, Dia.Month)));
        _motor.Caido = true;
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _descarga.GetDiotAsync(_cliente, Dia.Year, Dia.Month)));
        _motor.Caido = false;

        // La anual: las facturas del ejercicio y solo lo capturado; no se aceptan negativos; el cliente no la ve.
        var anual = await ComoAsync(Administrador, () => _descarga.CalcularAnualAsync(_cliente, Dia.Year, new CapturaDeAnual { PtuPagada = 1000, UmaAnual = 41000 }));
        anual.Resultado.ACargo.ShouldBe(100);
        var paraAnual = _motor.AnualRecibida.ShouldNotBeNull();
        paraAnual.Anio.ShouldBe(Dia.Year);
        paraAnual.Facturas.Count.ShouldBe(2);
        paraAnual.Captura.ShouldBe(new Dictionary<string, decimal> { ["ptu_pagada"] = 1000, ["uma_anual"] = 41000 });
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.CalcularAnualAsync(_cliente, Dia.Year, new CapturaDeAnual { PerdidasAnteriores = -1 })));
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _descarga.CalcularAnualAsync(_cliente, Dia.Year, new CapturaDeAnual())));

        // Un paquete de otro RFC no entra en este cliente.
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.SubirPaqueteAsync(_cliente, Paquete("BBB010101BBB", archivos, estatus))));

        // Quitar la marca.
        await ComoAsync(Administrador, () => _descarga.MarcarSinEntregaAsync(_cliente, Dia.Year, Dia.Month, false));
        (await ComoAsync(Administrador, () => _descarga.GetAsync(_cliente, Dia.Year, Dia.Month))).SinEntregaDelCliente.ShouldBeFalse();
    }

    [Fact]
    public async Task Un_xml_danado_se_cambia_por_el_bueno_del_sat()
    {
        // El cliente subió un XML que no trae timbre (la página de error del SAT guardada como .xml).
        await ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><html><body>Error</body></html>")),
                $"{Danada}.xml", "application/xml"),
            Dia.Year, Dia.Month));

        var estado = await ComoAsync(Administrador, () => _descarga.GetAsync(_cliente, Dia.Year, Dia.Month));
        estado.Meses.Last().Danadas.ShouldBe(1);
        estado.Danadas.ShouldHaveSingleItem().Nombre.ShouldContain(Danada);

        var orden = await ComoAsync(Administrador, () => _descarga.GetOrdenAsync(_cliente, Dia.Year, Dia.Month, Dia.Year, Dia.Month, false, true));
        orden.Danadas.ShouldBe(1);
        orden.Tengo.ShouldBe(0);

        var r = await ComoAsync(Administrador, () => _descarga.SubirPaqueteAsync(_cliente, Paquete(Rfc,
            new Dictionary<string, string> { [$"xml/recibidas/{Danada}.xml"] = Xml(Danada, "AAA010101AAA", Rfc, 580m) },
            $"{Danada},recibidas,Vigente,,,,,,\n")));
        r.Nuevas.ShouldBe(1);
        r.Reemplazadas.ShouldBe(1);

        estado = await ComoAsync(Administrador, () => _descarga.GetAsync(_cliente, Dia.Year, Dia.Month));
        estado.Meses.Last().Danadas.ShouldBe(0);
        estado.Meses.Last().Facturas.ShouldBe(1);
        estado.Danadas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_cfdi_de_retenciones_no_queda_danado_y_va_al_mes_de_su_periodo()
    {
        const string uuid = "12345678-1234-4234-8234-123456789012";
        var anio = Dia.Year - 1;
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?><retenciones:Retenciones xmlns:retenciones="http://www.sat.gob.mx/esquemas/retencionpago/2" Version="2.0" FechaExp="{Dia.Year}-02-10T10:00:00" CveRetenc="16" DescRetenc="Intereses"><retenciones:Emisor RfcE="BAN010101AAA" NomDenRazSocE="BANCO" RegimenFiscalE="601"/><retenciones:Receptor NacionalidadR="Nacional"><retenciones:Nacional RfcR="{Rfc}" NomDenRazSocR="CLIENTE" DomicilioFiscalR="62420"/></retenciones:Receptor><retenciones:Periodo MesIni="1" MesFin="12" Ejercicio="{anio}"/><retenciones:Totales MontoTotOperacion="5000.00" MontoTotGrav="5000" MontoTotExent="0" MontoTotRet="450"/><retenciones:Complemento><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" UUID="{uuid}"/></retenciones:Complemento></retenciones:Retenciones>
            """;
        await ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(xml.Trim())), "intereses.xml", "application/xml"),
            Dia.Year, Dia.Month));

        var d = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Documento, Guid>>().FirstAsync(x => x.OrganizacionId == _cliente && x.Uuid == uuid.ToUpperInvariant()));
        d.XmlDanado.ShouldBeFalse();
        d.FechaDelDocumento!.Value.Year.ShouldBe(anio);
        d.FechaDelDocumento!.Value.Month.ShouldBe(12);

        // No es una compra: no abre operación (si no, saldría como gasto en las pólizas).
        var op = await WithUnitOfWorkAsync(() => GetRequiredService<RegistroDeOperaciones>().DeDocumentoAsync(_cliente, d, Rfc));
        op.ShouldBeNull();
    }

    [Fact]
    public async Task Una_orden_pide_meses_validos_y_el_rfc()
    {
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.GetOrdenAsync(_cliente, Dia.Year, Dia.Month, Dia.Year - 3, 1, true, true)));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.GetOrdenAsync(_cliente, Dia.Year - 3, 1, Dia.Year, Dia.Month, true, true)));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.GetOrdenAsync(_cliente, Dia.Year, Dia.Month, Dia.Year, Dia.Month, false, false)));
        // Sin facturas no hay impuesto que calcular: se dice qué hacer.
        var e = await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.CalcularImpuestosAsync(_cliente, Dia.Year, Dia.Month)));
        e.Message.ShouldContain("Descargador 1");
        (await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.GetDiotAsync(_cliente, Dia.Year, Dia.Month)))).Message.ShouldContain("Descargador 1");
    }

    [Fact]
    public async Task La_orden_se_pide_por_dias_o_por_folio_y_el_estatus_dice_en_proceso_de_cancelacion()
    {
        // Una factura que ya está, y el SAT dice que tiene una solicitud de cancelación pendiente.
        var r = await ComoAsync(Administrador, () => _descarga.SubirPaqueteAsync(_cliente, Paquete(Rfc,
            new Dictionary<string, string> { [$"xml/recibidas/{Compra}.xml"] = Xml(Compra, "AAA010101AAA", Rfc, 1160m) },
            $"{Compra},recibidas,En proceso de cancelación,Cancelable con aceptación,,,,,,En proceso\n")));
        r.EnProceso.ShouldBe(1);
        r.Canceladas.ShouldBe(0);
        var mes = (await ComoAsync(Administrador, () => _descarga.GetAsync(_cliente, Dia.Year, Dia.Month))).Meses.Last();
        mes.EnProceso.ShouldBe(1);
        mes.Canceladas.ShouldBe(0);

        // Por días: el modo y los días van en la orden; «tengo» es lo de esos días.
        var dias = await ComoAsync(Administrador, () => _descarga.GetOrdenPorDiasAsync(_cliente, Dia, Dia.AddDays(6), true, false));
        dias.Tengo.ShouldBe(1);
        dias.NombreDeArchivo.ShouldContain($"del {Dia:yyyy-MM-dd} al {Dia.AddDays(6):yyyy-MM-dd}");
        using (var json = JsonDocument.Parse(dias.Contenido))
        {
            json.RootElement.GetProperty("modo").GetString().ShouldBe("dias");
            json.RootElement.GetProperty("desde").GetString().ShouldBe($"{Dia:yyyy-MM-dd}");
            json.RootElement.GetProperty("tipos").EnumerateArray().Select(t => t.GetString()).ShouldBe(["emitidas"]);
        }

        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.GetOrdenPorDiasAsync(_cliente, Dia, Dia.AddDays(-1), true, true)));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.GetOrdenPorDiasAsync(_cliente, Dia.AddYears(-2), Dia, true, true)));

        // Por folio: lo pegado puede traer texto alrededor; se quedan los UUID, sin repetir.
        var folios = await ComoAsync(Administrador, () => _descarga.CrearOrdenPorFoliosAsync(_cliente, new OrdenPorFoliosDto
        {
            Folios = [$"Factura {Compra.ToLowerInvariant()}, otra: {Venta}", Compra],
        }));
        folios.Tengo.ShouldBe(1);
        using (var json = JsonDocument.Parse(folios.Contenido))
        {
            json.RootElement.GetProperty("modo").GetString().ShouldBe("folios");
            json.RootElement.GetProperty("folios").EnumerateArray().Select(t => t.GetString()).ShouldBe([Compra, Venta]);
        }

        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _descarga.CrearOrdenPorFoliosAsync(_cliente, new OrdenPorFoliosDto { Folios = ["nada"] })));
    }
}
