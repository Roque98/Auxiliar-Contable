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
}
