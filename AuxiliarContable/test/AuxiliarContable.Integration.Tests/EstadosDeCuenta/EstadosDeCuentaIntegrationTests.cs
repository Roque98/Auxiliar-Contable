using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Documentos;
using AuxiliarContable.EstadosDeCuenta;
using AuxiliarContable.IA;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Estados de cuenta contra PostgreSQL real con la IA de prueba: subir → leer → cuadrar →
/// corregir → confirmar, y que lo ajeno no se ve.
/// </summary>
public class EstadosDeCuentaIntegrationTests : ConJerarquiaTestBase
{
    private readonly IBovedaAppService _boveda;
    private readonly IEstadosDeCuentaAppService _estados;
    private readonly ModeloDeIADePrueba _ia;
    private Guid _cliente;
    private Guid _otroCliente;

    public EstadosDeCuentaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _boveda = GetRequiredService<IBovedaAppService>();
        _estados = GetRequiredService<IEstadosDeCuentaAppService>();
        _ia = (ModeloDeIADePrueba)GetRequiredService<IModeloDeIA>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _ia.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
    }

    private async Task<Guid> SubirYLeerAsync(object lectura)
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 estado de cuenta " + Guid.NewGuid());
        var subido = await ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.EstadoDeCuenta,
            new RemoteStreamContent(new MemoryStream(pdf), "bbva-oct.pdf")));
        _ia.Responder(lectura);
        await WithUnitOfWorkAsync(() => GetRequiredService<ProcesadorDeEstadoDeCuenta>().ProcesarAsync(subido.Documento.Id));
        return subido.Documento.Id;
    }

    private static object Lectura(decimal segundoCargo) => new
    {
        esEstadoDeCuenta = true,
        banco = "BBVA",
        cuenta = "**** 1234",
        moneda = "MXN",
        periodoInicio = "2026-10-01",
        periodoFin = "2026-10-31",
        saldoInicial = 10000m,
        saldoFinal = 11749.50m,
        movimientos = new object[]
        {
            new { fecha = "2026-10-03", concepto = "DEPOSITO CLIENTE ACME", cargo = 0m, abono = 5000m, saldo = 15000m },
            new { fecha = "2026-10-07", concepto = "PAGO CFE", cargo = segundoCargo, abono = 0m, saldo = 13749.50m },
            new { fecha = "2026-10-15", concepto = "TRANSFERENCIA PROVEEDOR", cargo = 2000m, abono = 0m, saldo = 11749.50m },
            new { fecha = "sin fecha", concepto = "renglón ilegible", cargo = 1m, abono = 0m },
        },
    };

    [Fact]
    public async Task Se_lee_cuadra_y_se_confirma()
    {
        var documentoId = await SubirYLeerAsync(Lectura(1250.50m));

        var lista = await ComoAsync(Usuario, () => _estados.GetListAsync(_cliente));
        var resumen = lista.ShouldHaveSingleItem();
        resumen.DocumentoId.ShouldBe(documentoId);
        resumen.Cuadra.ShouldBeTrue();
        resumen.Movimientos.ShouldBe(3, "el renglón sin fecha se descarta");
        resumen.Cuenta.ShouldBe("1234");

        var confirmado = await ComoAsync(Usuario, () => _estados.ConfirmarAsync(_cliente, resumen.Id, aunqueNoCuadre: false));
        confirmado.Revision.ShouldBe(RevisionDelEstadoDeCuenta.Confirmado);

        // El documento queda fechado al corte y procesado.
        var enBoveda = await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, Anio = 2026, Mes = 10 }));
        enBoveda.Items.ShouldHaveSingleItem().Estado.ShouldBe(EstadoDeDocumento.Procesado);
        _ia.Solicitudes.ShouldHaveSingleItem().Proposito.ShouldBe(ProcesadorDeEstadoDeCuenta.Proposito);
    }

    [Fact]
    public async Task Si_no_cuadra_pide_revision_y_se_corrige_el_renglon()
    {
        await SubirYLeerAsync(Lectura(1520.50m));
        var id = (await ComoAsync(Usuario, () => _estados.GetListAsync(_cliente))).Single().Id;

        var estado = await ComoAsync(Usuario, () => _estados.GetDetalleAsync(_cliente, id));
        estado.Revision.ShouldBe(RevisionDelEstadoDeCuenta.RequiereRevision);
        estado.Diferencia.ShouldBe(-270m);
        var malo = estado.Renglones.Single(r => r.SaltoDeSaldo);
        malo.Concepto.ShouldBe("PAGO CFE");

        var corregido = await ComoAsync(Usuario, () => _estados.CorregirMovimientoAsync(_cliente, id, new CorregirMovimientoDto
        {
            MovimientoId = malo.Id, Fecha = malo.Fecha, Concepto = malo.Concepto, Cargo = 1250.50m, Abono = 0,
        }));

        corregido.Cuadra.ShouldBeTrue();
        corregido.Revision.ShouldBe(RevisionDelEstadoDeCuenta.PorConfirmar);
        corregido.Renglones.ShouldAllBe(r => !r.SaltoDeSaldo);
    }

    [Fact]
    public async Task Lo_que_no_es_estado_de_cuenta_queda_con_error()
    {
        var documentoId = await SubirYLeerAsync(new { esEstadoDeCuenta = false, movimientos = Array.Empty<object>() });

        (await ComoAsync(Usuario, () => _estados.GetListAsync(_cliente))).ShouldBeEmpty();
        var doc = (await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente }))).Items.Single(d => d.Id == documentoId);
        doc.Estado.ShouldBe(EstadoDeDocumento.ConError);
        doc.Motivo!.ShouldContain("No parece");
    }

    [Fact]
    public async Task Lo_ajeno_no_se_ve()
    {
        await SubirYLeerAsync(Lectura(1250.50m));
        var id = (await ComoAsync(Usuario, () => _estados.GetListAsync(_cliente))).Single().Id;

        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _estados.GetListAsync(_otroCliente)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Administrador, () => _estados.GetDetalleAsync(_otroCliente, id)));
    }
}
