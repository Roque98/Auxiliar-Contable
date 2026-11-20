using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Bancos;
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
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Mesa de trabajo del despacho: Claude lee el control interno y completa su operación (sin repetir lo ya leído), y el
/// banco conciliado se amarra por UUID, por regla y con Claude (lo dudoso queda como sugerencia). El cliente no entra.
/// </summary>
public class MesaDeTrabajoIntegrationTests : ConJerarquiaTestBase
{
    private const string Uuid = "6F9619FF-8B86-D011-B42D-00C04FC964FF";

    private readonly IMesaDeTrabajoAppService _mesa;
    private readonly IOperacionesAppService _operaciones;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    private static DateTime Dia => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    public MesaDeTrabajoIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _mesa = GetRequiredService<IMesaDeTrabajoAppService>();
        _operaciones = GetRequiredService<IOperacionesAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Constructora Lupita");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        var perfil = GetRequiredService<IPerfilFiscalAppService>();
        await ComoAsync(Administrador, () => perfil.ResponderAsync(_cliente, new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = "GODE561231GR8" }));
        await ComoAsync(Administrador, () => perfil.ResponderAsync(_cliente, new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Giro, Valor = "Construcción de casas" }));
    }

    private static string Xml(string uuid, decimal total) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{Dia:yyyy-MM-dd}T10:00:00" Total="{total:0.00}" TipoDeComprobante="I"><cfdi:Emisor Rfc="AAA010101AAA" Nombre="MATERIALES DEL BAJIO"/><cfdi:Receptor Rfc="GODE561231GR8" Nombre="CONSTRUCTORA"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Mortero 50 kg"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>""";

    private async Task<Guid> SubirAsync(string nombre, TipoDeDocumento tipo, string contenido)
    {
        var r = await ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, tipo,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(contenido)), nombre, "application/octet-stream"),
            Dia.Year, Dia.Month));
        return r.Documento.Id;
    }

    [Fact]
    public async Task Claude_lee_el_control_interno_y_completa_su_operacion()
    {
        await SubirAsync("mortero.xml", TipoDeDocumento.Cfdi, Xml(Uuid, 435.50m));
        await SubirAsync("nota.pdf", TipoDeDocumento.ControlInterno, "%PDF-1.4 nota");
        _motor.Analisis["nota.pdf"] = new AnalisisDeDocumento
        {
            Legible = true, Tipo = "nota_de_compra", Fecha = Dia.AddDays(2).ToString("yyyy-MM-dd"), Importe = 435.50m, Uuid = Uuid.ToLowerInvariant(),
            Referencia = "AUT 778812", FormaDePago = "tarjeta", Sentido = "salida", QueEs = "Dos sacos de mortero",
            RelacionConGiro = "insumo_directo", Explicacion = "Se integra a la obra.", QuePrueba = "La compra al proveedor.", Confianza = "alta",
        };

        // El cliente no entra a la mesa.
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _mesa.GetAsync(_cliente, Dia.Year, Dia.Month)));

        (await ComoAsync(Administrador, () => _mesa.GetAsync(_cliente, Dia.Year, Dia.Month))).PorAnalizar.ShouldBe(1);
        var r = await ComoAsync(Administrador, () => _mesa.AnalizarAsync(_cliente, Dia.Year, Dia.Month));

        r.Analizados.ShouldBe(1);
        r.Completadas.ShouldBe(1);
        r.Faltan.ShouldBe(0);
        _motor.GiroRecibido.ShouldBe("Construcción de casas");
        // La nota trae el UUID: se juntó con la operación de su factura, y trae la autorización del pago.
        var mesa = await ComoAsync(Administrador, () => _mesa.GetAsync(_cliente, Dia.Year, Dia.Month));
        var op = mesa.Operaciones.Operaciones.ShouldHaveSingleItem();
        op.Factura.Referencia.ShouldBe(Uuid);
        op.ControlInterno.Estado.ShouldBe(EstadoDePata.Ligada);
        op.ControlInterno.Analisis!.RelacionConGiro.ShouldBe("insumo_directo");
        op.Transaccion.Referencia.ShouldBe("AUT 778812");
        mesa.PorAnalizar.ShouldBe(0);
        mesa.Analizados.ShouldBe(1);

        // Lo ya leído no se vuelve a mandar.
        (await ComoAsync(Administrador, () => _mesa.AnalizarAsync(_cliente, Dia.Year, Dia.Month))).Analizados.ShouldBe(0);
        _motor.Analizados.ShouldBe(["nota.pdf"]);
    }

    [Fact]
    public async Task El_banco_se_amarra_por_uuid_por_regla_y_con_claude()
    {
        await SubirAsync("mortero.xml", TipoDeDocumento.Cfdi, Xml(Uuid, 1160m));
        await SubirAsync("banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        // Lo que el cliente ya dio sin pago: un gasto de $480, una venta de $1,990 y papelería de $100.
        foreach (var (desc, sentido, importe) in new[]
                 {
                     ("Clavos", SentidoDeOperacion.Salida, 480m), ("Venta de puerta", SentidoDeOperacion.Entrada, 1990m),
                     ("Papelería", SentidoDeOperacion.Salida, 100m),
                 })
        {
            await ComoAsync(Usuario, () => _operaciones.CrearAsync(_cliente, new GuardarOperacionDto
            {
                Fecha = Dia, Sentido = sentido, Importe = importe, Descripcion = desc,
                ControlInterno = new GuardarPataDto { Estado = EstadoDePata.NoHay, Motivo = "No tengo comprobante interno" },
            }));
        }

        MovimientoLeido Mov(string concepto, string naturaleza, decimal importe, string estado, params string[] facturas) => new()
        {
            Fecha = Dia.AddDays(4).ToString("dd/MM/yyyy"), Concepto = concepto, Naturaleza = naturaleza, Importe = importe, Cuenta = "0123",
            Referencia = concepto[..4], Conciliacion = new ConciliacionDeMovimiento { Estado = estado, Facturas = [.. facturas] },
        };
        _motor.Conciliacion = new ResultadoDeConciliacion
        {
            Cuentas = [new CuentaLeida { Banco = "BANORTE", Cuenta = "0123", Cuadra = true }],
            Movimientos =
            [
                Mov("SPEI MATERIALES", "cargo", 1160m, "conciliado", Uuid),
                Mov("PAGO FERRETERIA", "cargo", 480m, "sin_factura"),
                Mov("DEPOSITO CLIENTE", "abono", 2000m, "sin_factura"),
                Mov("OXXO PAPELERIA", "cargo", 99m, "sin_factura"),
                Mov("TRASPASO A 0999", "cargo", 5000m, "traspaso"),
            ],
        };
        await ComoAsync(Administrador, () => GetRequiredService<IBancosAppService>().ConciliarAsync(_cliente, Dia.Year, Dia.Month));
        _motor.Amarres = (movs, candidatas) => new SugerenciasDeAmarre
        {
            Pares =
            [
                new ParDeAmarre { Movimiento = movs.Single(m => m.Importe == 2000m).Id, Operacion = candidatas.Single(c => c.Importe == 1990m).Id,
                    Confianza = "alta", Porque = "Depósito del cliente menos comisión" },
                new ParDeAmarre { Movimiento = movs.Single(m => m.Importe == 99m).Id, Operacion = candidatas.Single(c => c.Importe == 100m).Id,
                    Confianza = "media", Porque = "Importe casi igual" },
            ],
        };

        var r = await ComoAsync(Administrador, () => _mesa.AmarrarBancoAsync(_cliente, Dia.Year, Dia.Month));

        r.OperacionesNuevas.ShouldBe(4);
        r.PorUuid.ShouldBe(1);
        r.PorRegla.ShouldBe(1);
        r.PorClaude.ShouldBe(1);
        var sugerencia = r.Sugerencias.ShouldHaveSingleItem();
        sugerencia.Confianza.ShouldBe("media");

        var mesa = await ComoAsync(Administrador, () => _mesa.GetAsync(_cliente, Dia.Year, Dia.Month));
        mesa.MovimientosDelBanco.ShouldBe(4);
        mesa.MovimientosComoOperacion.ShouldBe(4);
        var ops = mesa.Operaciones.Operaciones;
        ops.Single(o => o.Factura.Referencia == Uuid).Transaccion.DelBanco.ShouldBeTrue();
        ops.Single(o => o.Descripcion == "Clavos").Transaccion.DelBanco.ShouldBeTrue();
        ops.Single(o => o.Descripcion == "Venta de puerta").Transaccion.DelBanco.ShouldBeTrue();
        ops.Single(o => o.Descripcion == "Papelería").Transaccion.Estado.ShouldBe(EstadoDePata.Pendiente);

        // Aceptar la sugerencia es juntar; y volver a amarrar no duplica nada.
        await ComoAsync(Administrador, () => _operaciones.JuntarAsync(_cliente, sugerencia.OperacionId, sugerencia.MovimientoId));
        var otra = await ComoAsync(Administrador, () => _mesa.AmarrarBancoAsync(_cliente, Dia.Year, Dia.Month));
        otra.OperacionesNuevas.ShouldBe(0);
        (await ComoAsync(Administrador, () => _mesa.GetAsync(_cliente, Dia.Year, Dia.Month))).Operaciones.Operaciones.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Las_polizas_se_generan_con_el_perfil_y_lo_aceptado_se_manda()
    {
        var perfil = GetRequiredService<IPerfilFiscalAppService>();
        // Un campo de opción solo acepta sus valores.
        await Should.ThrowAsync<Volo.Abp.BusinessException>(() => ComoAsync(Administrador, () => perfil.ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.TipoDeEmpresa, Valor = "tienda" })));
        await ComoAsync(Administrador, () => perfil.ResponderAsync(_cliente, new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.TipoDeEmpresa, Valor = "Constructora" }));
        await ComoAsync(Administrador, () => perfil.ResponderAsync(_cliente, new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.SistemaDeCostos, Valor = "ordenes" }));
        (await ComoAsync(Administrador, () => perfil.GetAsync(_cliente))).Campos.Single(c => c.Clave == CamposDelPerfil.TipoDeEmpresa).Opciones!.Count.ShouldBe(5);

        await SubirAsync("mortero.xml", TipoDeDocumento.Cfdi, Xml(Uuid, 1160m));
        var op = (await ComoAsync(Administrador, () => _operaciones.GetListAsync(_cliente, Dia.Year, Dia.Month))).Operaciones.Single();
        _motor.Contabilidad = (p, ops, xmls) => new ResultadoContable
        {
            Polizas = p.TipoEmpresa == "constructora" && xmls.Count == 1
                ? [new PolizaContable { Operacion = ops[0].Id, Tipo = "Diario", Numero = 1, Movimientos = [new MovimientoContable { Cuenta = "115.03", Debe = 1000 }] }]
                : [],
            Revision = ops[0].Aceptados.Contains("AJENO_AL_GIRO")
                ? []
                : [new RenglonDeRevision { Operacion = ops[0].Id, Codigo = "AJENO_AL_GIRO", Nivel = "detiene", Mensaje = "No parece del giro" }],
        };

        // El cliente no genera pólizas.
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _mesa.GenerarPolizasAsync(_cliente, Dia.Year, Dia.Month, new DatosDeCierre())));
        (await ComoAsync(Administrador, () => _mesa.GetPolizasAsync(_cliente, Dia.Year, Dia.Month))).ShouldBeNull();

        var r = await ComoAsync(Administrador, () => _mesa.GenerarPolizasAsync(_cliente, Dia.Year, Dia.Month, new DatosDeCierre()));
        r.Detenidas.ShouldBe(1);
        r.Resultado.Polizas.ShouldHaveSingleItem().Movimientos[0].Cuenta.ShouldBe("115.03");
        var mandada = _motor.Contabilizadas.ShouldHaveSingleItem();
        mandada.Id.ShouldBe(op.Id.ToString());
        mandada.Factura.Uuid.ShouldBe(Uuid);
        mandada.Sentido.ShouldBe("salida");

        await ComoAsync(Administrador, () => _mesa.AceptarRevisionAsync(_cliente, op.Id, "ajeno_al_giro"));
        var otra = await ComoAsync(Administrador, () => _mesa.GenerarPolizasAsync(_cliente, Dia.Year, Dia.Month, new DatosDeCierre()));
        _motor.Contabilizadas[0].Aceptados.ShouldBe(["AJENO_AL_GIRO"]);
        otra.Detenidas.ShouldBe(0);
        (await ComoAsync(Administrador, () => _mesa.GetPolizasAsync(_cliente, Dia.Year, Dia.Month)))!.Resultado.Polizas.Count.ShouldBe(1);
    }

    [Fact]
    public async Task El_pago_de_una_compra_del_mes_anterior_entra_y_el_inventario_pasa_al_mes_siguiente()
    {
        var antes = Dia.AddMonths(-1);
        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.TipoDeEmpresa, Valor = "comercial" }));
        // Una compra a crédito del mes anterior que se paga por el banco en este.
        await ComoAsync(Usuario, () => _operaciones.CrearAsync(_cliente, new GuardarOperacionDto
        {
            Fecha = antes.AddDays(20), Sentido = SentidoDeOperacion.Salida, Importe = 480m, Descripcion = "Clavos",
            ControlInterno = new GuardarPataDto { Estado = EstadoDePata.NoHay, Motivo = "No tengo comprobante interno" },
        }));
        await SubirAsync("banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        _motor.Conciliacion = new ResultadoDeConciliacion
        {
            Cuentas = [new CuentaLeida { Banco = "BANORTE", Cuenta = "0123", Cuadra = true }],
            Movimientos =
            [
                new MovimientoLeido { Fecha = Dia.AddDays(3).ToString("dd/MM/yyyy"), Concepto = "PAGO FERRETERIA", Naturaleza = "cargo", Importe = 480m,
                    Cuenta = "0123", Conciliacion = new ConciliacionDeMovimiento { Estado = "sin_factura" } },
            ],
        };
        await ComoAsync(Administrador, () => GetRequiredService<IBancosAppService>().ConciliarAsync(_cliente, Dia.Year, Dia.Month));
        (await ComoAsync(Administrador, () => _mesa.AmarrarBancoAsync(_cliente, Dia.Year, Dia.Month))).PorRegla.ShouldBe(1);

        // El mes anterior se cierra con inventario final de 4,500.
        await ComoAsync(Administrador, () => _mesa.GenerarPolizasAsync(_cliente, antes.Year, antes.Month,
            new DatosDeCierre { InventarioInicial = 5000m, InventarioFinal = 4500m }));
        _motor.PeriodoRecibido.ShouldBe($"{antes:yyyy-MM}");
        _motor.CierreRecibido!.InventarioFinal.ShouldBe(4500m);

        var r = await ComoAsync(Administrador, () => _mesa.GenerarPolizasAsync(_cliente, Dia.Year, Dia.Month, new DatosDeCierre()));
        var clavos = _motor.Contabilizadas.ShouldHaveSingleItem();
        clavos.Descripcion.ShouldBe("Clavos");
        clavos.Fecha.ShouldBe(antes.AddDays(20).ToString("yyyy-MM-dd"));
        clavos.Transaccion.Importe.ShouldBe(480m);
        clavos.Transaccion.Fecha.ShouldBe(Dia.AddDays(3).ToString("yyyy-MM-dd"));
        r.InventarioInicialSugerido.ShouldBe(4500m);
    }

    [Fact]
    public async Task Apertura_balanza_acumulada_y_contabilidad_electronica()
    {
        var apertura = Dia.AddMonths(-2);
        // Saldos que no cuadran no se guardan.
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _mesa.GuardarAperturaAsync(_cliente, new SaldosDeAperturaDto
        {
            Anio = apertura.Year, Mes = apertura.Month, Saldos = [new SaldoDeApertura { Cuenta = "102.01", Deudor = 100 }],
        })));
        await ComoAsync(Administrador, () => _mesa.GuardarAperturaAsync(_cliente, new SaldosDeAperturaDto
        {
            Anio = apertura.Year, Mes = apertura.Month,
            Saldos = [new SaldoDeApertura { Cuenta = "102.01", Deudor = 50000 }, new SaldoDeApertura { Cuenta = "301.01", Acreedor = 50000 }],
        }));
        (await ComoAsync(Administrador, () => _mesa.GetAperturaAsync(_cliente)))!.Saldos.Count.ShouldBe(2);
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _mesa.GetAperturaAsync(_cliente)));

        // Sin pólizas del mes no hay contabilidad electrónica.
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _mesa.ContabilidadElectronicaAsync(_cliente, Dia.Year, Dia.Month)));

        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.TipoDeEmpresa, Valor = "servicios" }));
        _motor.Contabilidad = (_, _, _) => new ResultadoContable
        {
            Polizas = [new PolizaContable { Operacion = "x", Tipo = "Diario", Numero = 1, Fecha = Dia.ToString("yyyy-MM-dd"), Rfc = "AAA010101AAA",
                Movimientos = [new MovimientoContable { Cuenta = "601.84", Debe = 10 }, new MovimientoContable { Cuenta = "102.01", Haber = 10 }] }],
        };
        await ComoAsync(Administrador, () => _mesa.GenerarPolizasAsync(_cliente, Dia.Year, Dia.Month, new DatosDeCierre()));

        var r = await ComoAsync(Administrador, () => _mesa.ContabilidadElectronicaAsync(_cliente, Dia.Year, Dia.Month));
        r.Resultado.Archivos.ShouldHaveSingleItem().Nombre.ShouldBe($"GODE561231GR8{Dia:yyyyMM}BN.xml");
        var (aperturaMandada, meses) = _motor.Electronica!.Value;
        aperturaMandada!.Periodo.ShouldBe($"{apertura:yyyy-MM}");
        meses.ShouldHaveSingleItem().Polizas.Single().Rfc.ShouldBe("AAA010101AAA");
        // Entre la apertura y este mes faltan dos meses de pólizas.
        r.Resultado.Avisos.Count(a => a.StartsWith("Faltan las pólizas")).ShouldBe(2);
        _motor.CatalogoRecibido.ShouldBeNull();

        // Con el catálogo propio del cliente (XML, que no por ser XML es factura), el motor lo recibe: el más reciente.
        const string catalogo = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><catalogocuentas:Catalogo xmlns:catalogocuentas=\"http://www.sat.gob.mx/esquemas/ContabilidadE/1_3/CatalogoCuentas\" Version=\"1.3\" RFC=\"GODE561231GR8\" Mes=\"01\" Anio=\"2026\"><catalogocuentas:Ctas CodAgrup=\"102.01\" NumCta=\"1100-001\" Desc=\"Bancos\" Nivel=\"1\" Natur=\"D\"/></catalogocuentas:Catalogo>";
        var subido = await ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.CatalogoDeCuentas,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(catalogo)), "catalogo.xml", "application/xml"), Dia.Year, Dia.Month));
        subido.Documento.Tipo.ShouldBe(TipoDeDocumento.CatalogoDeCuentas);
        subido.Aviso.ShouldBeNull();
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente,
            TipoDeDocumento.CatalogoDeCuentas, new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 x")), "catalogo.pdf", "application/pdf"))));
        await ComoAsync(Administrador, () => _mesa.ContabilidadElectronicaAsync(_cliente, Dia.Year, Dia.Month));
        _motor.CatalogoRecibido.ShouldNotBeNull().Nombre.ShouldBe("catalogo.xml");
        Encoding.UTF8.GetString(_motor.CatalogoRecibido.Contenido).ShouldContain("1100-001");
    }
}
