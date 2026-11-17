using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Bancos;
using AuxiliarContable.Boveda;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.Identity;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Control interno: cada salida de dinero del mes dice qué le falta (materialidad si tiene factura, comprobante o
/// testimonio si no), la evidencia la completa y sobrevive a otra conciliación, lo ajeno no existe, un mes cerrado no
/// se toca y el cierre del mes lo resume.
/// </summary>
public class ControlInternoIntegrationTests : ConJerarquiaTestBase
{
    private readonly IControlInternoAppService _control;
    private readonly IBancosAppService _bancos;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;
    private Guid _otroCliente;
    private IdentityUser _lector = null!;

    private static int Anio => DateTime.Now.Year;
    private static int Mes => DateTime.Now.Month;

    public ControlInternoIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _control = GetRequiredService<IControlInternoAppService>();
        _bancos = GetRequiredService<IBancosAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        _lector = await CrearUsuarioAsync("ana", AuxiliarRoles.Usuario);
        await WithUnitOfWorkAsync(async () =>
        {
            var m = GetRequiredService<MembresiasDeOrganizacion>();
            await m.AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro);
            await m.AgregarAsync(_cliente, _lector.Id, RolEnOrganizacion.Lector);
        });
    }

    private static MovimientoLeido Mov(string concepto, string naturaleza, decimal importe, string estado, params string[] facturas) => new()
    {
        Fecha = "05/11/2026", Concepto = concepto, Naturaleza = naturaleza, Importe = importe, Cuenta = "0123",
        Conciliacion = new ConciliacionDeMovimiento { Estado = estado, Facturas = [.. facturas] },
    };

    private Task<ResultadoDeSubidaDto> SubirAsync(Guid cliente, string nombre, TipoDeDocumento tipo, string contenido) =>
        ComoAsync(Administrador, () => GetRequiredService<IBovedaAppService>().SubirAsync(cliente, tipo,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(contenido)), nombre, "application/octet-stream")));

    /// <summary>Concilia el mes actual con 4 movimientos: un gasto con factura, uno sin factura, un cobro y un traspaso.</summary>
    private async Task ConciliarAsync()
    {
        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = "GODE561231GR8" }));
        await SubirAsync(_cliente, "banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        _motor.Conciliacion = new ResultadoDeConciliacion
        {
            Cuentas = [new CuentaLeida { Banco = "BANORTE", Cuenta = "0123", Cuadra = true }],
            Movimientos =
            [
                Mov("SPEI ENVIADO CONSULTORES SA", "cargo", 34800m, "conciliado", "U-SERVICIO"),
                Mov("RETIRO CAJERO", "cargo", 2000m, "sin_factura"),
                Mov("SPEI RECIBIDO ACME", "abono", 11600m, "conciliado", "U-VENTA"),
                Mov("TRASPASO A CTA 0999", "cargo", 5000m, "traspaso"),
            ],
        };
        await ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, Anio, Mes));
    }

    private async Task<OperacionDto> OperacionAsync(string concepto) =>
        (await ComoAsync(Usuario, () => _control.GetAsync(_cliente, Anio, Mes))).Operaciones.Single(o => o.Descripcion == concepto);

    private AgregarEvidenciaDto ABanco(string clave, TipoDeEvidencia tipo, string? nota = "x", Guid? documento = null) => new()
    {
        Anio = Anio, Mes = Mes, Origen = OrigenDeOperacion.Banco, Clave = clave, Tipo = tipo, Nota = nota, DocumentoId = documento,
    };

    [Fact]
    public async Task Cada_salida_dice_que_le_falta_y_las_entradas_y_traspasos_no_piden_nada()
    {
        await ConciliarAsync();

        var r = await ComoAsync(Usuario, () => _control.GetAsync(_cliente, Anio, Mes));

        r.HayConciliacion.ShouldBeTrue();
        r.Operaciones.Single(o => o.Descripcion.StartsWith("SPEI ENVIADO")).Estado.ShouldBe(EstadoDeEvidencia.FaltaMaterialidad);
        r.Operaciones.Single(o => o.Descripcion == "RETIRO CAJERO").Estado.ShouldBe(EstadoDeEvidencia.FaltaComprobante);
        r.Operaciones.Single(o => o.Descripcion.StartsWith("SPEI RECIBIDO")).Estado.ShouldBe(EstadoDeEvidencia.NoAplica);
        r.Operaciones.Single(o => o.Descripcion.StartsWith("TRASPASO")).Estado.ShouldBe(EstadoDeEvidencia.NoAplica);
        r.FaltaMaterialidad.ShouldBe(1);
        r.FaltaComprobante.ShouldBe(1);
        r.PuedeAgregar.ShouldBeTrue();
    }

    [Fact]
    public async Task La_evidencia_completa_la_operacion_y_sobrevive_a_otra_conciliacion()
    {
        await ConciliarAsync();
        var servicio = await OperacionAsync("SPEI ENVIADO CONSULTORES SA");
        var retiro = await OperacionAsync("RETIRO CAJERO");

        // Un comprobante de pago no prueba que el servicio ocurrió; el contrato sí.
        await ComoAsync(Usuario, () => _control.AgregarAsync(_cliente, ABanco(servicio.Clave!, TipoDeEvidencia.ComprobanteDePago)));
        (await OperacionAsync("SPEI ENVIADO CONSULTORES SA")).Estado.ShouldBe(EstadoDeEvidencia.FaltaMaterialidad);
        var contrato = await SubirAsync(_cliente, "contrato.pdf", TipoDeDocumento.Otro, "%PDF-1.4 contrato");
        await ComoAsync(Usuario, () => _control.AgregarAsync(_cliente, ABanco(servicio.Clave!, TipoDeEvidencia.Contrato, null, contrato.Documento.Id)));
        await ComoAsync(Usuario, () => _control.AgregarAsync(_cliente,
            ABanco(retiro.Clave!, TipoDeEvidencia.Testimonio, "Pago en efectivo al albañil por la reparación del horno")));

        // Se vuelve a conciliar con lo mismo: la evidencia sigue ahí.
        await ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, Anio, Mes));
        var r = await ComoAsync(Usuario, () => _control.GetAsync(_cliente, Anio, Mes));

        var completo = r.Operaciones.Single(o => o.Clave == servicio.Clave);
        completo.Estado.ShouldBe(EstadoDeEvidencia.Completa);
        completo.Evidencias.Count.ShouldBe(2);
        completo.Evidencias.ShouldContain(e => e.NombreDelDocumento == "contrato.pdf");
        r.Operaciones.Single(o => o.Clave == retiro.Clave).Estado.ShouldBe(EstadoDeEvidencia.Completa);
        r.FaltaMaterialidad.ShouldBe(0);
        r.FaltaComprobante.ShouldBe(0);

        var quitar = completo.Evidencias.Single(e => e.Tipo == TipoDeEvidencia.Contrato).Id;
        await ComoAsync(Usuario, () => _control.QuitarAsync(_cliente, quitar));
        (await OperacionAsync("SPEI ENVIADO CONSULTORES SA")).Estado.ShouldBe(EstadoDeEvidencia.FaltaMaterialidad);
    }

    [Fact]
    public async Task Efectivo_y_credito_tambien_entra_y_su_comprobante_cuenta()
    {
        var dinero = GetRequiredService<IDineroFueraDelBancoAppService>();
        var ticket = await SubirAsync(_cliente, "ticket.pdf", TipoDeDocumento.Ticket, "%PDF-1.4 ticket");
        var hoy = DateTime.Now.Date;
        await ComoAsync(Usuario, () => dinero.CrearAsync(_cliente, new GuardarMovimientoFueraDelBancoDto
        {
            Tipo = TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo, Fecha = hoy, Importe = 300m, Concepto = "Gas", DocumentoId = ticket.Documento.Id,
        }));
        var sinNada = await ComoAsync(Usuario, () => dinero.CrearAsync(_cliente, new GuardarMovimientoFueraDelBancoDto
        {
            Tipo = TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo, Fecha = hoy, Importe = 150m, Concepto = "Propinas",
        }));

        var r = await ComoAsync(Usuario, () => _control.GetAsync(_cliente, Anio, Mes));

        r.HayConciliacion.ShouldBeFalse();
        r.Operaciones.Single(o => o.Descripcion == "Gas").Estado.ShouldBe(EstadoDeEvidencia.Completa);
        r.Operaciones.Single(o => o.Descripcion == "Propinas").Estado.ShouldBe(EstadoDeEvidencia.FaltaComprobante);

        await ComoAsync(Usuario, () => _control.AgregarAsync(_cliente, new AgregarEvidenciaDto
        {
            Anio = Anio, Mes = Mes, Origen = OrigenDeOperacion.FueraDelBanco, MovimientoFueraDelBancoId = sinNada.Id,
            Tipo = TipoDeEvidencia.Testimonio, Nota = "Propinas a los repartidores",
        }));
        (await ComoAsync(Usuario, () => _control.GetAsync(_cliente, Anio, Mes))).FaltaComprobante.ShouldBe(0);
    }

    [Fact]
    public async Task No_se_cuelga_evidencia_vacia_inventada_ajena_ni_en_un_mes_cerrado()
    {
        await ConciliarAsync();
        var servicio = await OperacionAsync("SPEI ENVIADO CONSULTORES SA");

        await Should.ThrowAsync<BusinessException>(() =>
            ComoAsync(Usuario, () => _control.AgregarAsync(_cliente, ABanco(servicio.Clave!, TipoDeEvidencia.Contrato, null))));
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _control.AgregarAsync(_cliente, ABanco("no-existe", TipoDeEvidencia.Contrato))));
        var ajeno = await SubirAsync(_otroCliente, "ajeno.pdf", TipoDeDocumento.Otro, "%PDF-1.4 ajeno");
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(Usuario, () => _control.AgregarAsync(_cliente, ABanco(servicio.Clave!, TipoDeEvidencia.Contrato, null, ajeno.Documento.Id))));
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(_lector, () => _control.AgregarAsync(_cliente, ABanco(servicio.Clave!, TipoDeEvidencia.Contrato))));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _control.GetAsync(_otroCliente, Anio, Mes)));

        await WithUnitOfWorkAsync(async () =>
        {
            var p = new PeriodoContable(Guid.NewGuid(), _cliente, Anio, Mes);
            p.Cerrar(Administrador.Id, DateTime.UtcNow);
            await GetRequiredService<IRepository<PeriodoContable, Guid>>().InsertAsync(p, autoSave: true);
        });
        (await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _control.AgregarAsync(_cliente, ABanco(servicio.Clave!, TipoDeEvidencia.Contrato)))))
            .Message.ShouldContain("ya está cerrado");
        (await ComoAsync(Usuario, () => _control.GetAsync(_cliente, Anio, Mes))).PuedeAgregar.ShouldBeFalse();
    }

    [Fact]
    public async Task El_cierre_del_mes_cuenta_lo_que_falta()
    {
        await ConciliarAsync();

        var cierre = await ComoAsync(Administrador, () => GetRequiredService<ICierreAppService>().GetAsync(_cliente, Anio, Mes));

        var renglon = cierre.Revision.Single(r => r.Texto.StartsWith("Control interno"));
        renglon.Texto.ShouldContain("1 gasto(s) con factura sin evidencia");
        renglon.Bloquea.ShouldBeFalse();
    }
}
