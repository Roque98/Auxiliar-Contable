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
    }
}
