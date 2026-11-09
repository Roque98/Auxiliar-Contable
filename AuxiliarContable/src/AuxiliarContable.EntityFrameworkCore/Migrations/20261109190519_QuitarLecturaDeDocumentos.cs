using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class QuitarLecturaDeDocumentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCfdis");

            migrationBuilder.DropTable(
                name: "AppConciliaciones");

            migrationBuilder.DropTable(
                name: "AppMovimientosBancarios");

            migrationBuilder.DropTable(
                name: "AppEstadosDeCuenta");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppCfdis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Concepto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Descuento = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmisorNombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    EmisorRfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Folio = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FormaDePago = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetodoDePago = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceptorNombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReceptorRfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    Serie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SubTotal = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    TipoDeCambio = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    TipoDeComprobante = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    TotalRetenidos = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    TotalTrasladados = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    UsoCfdi = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Version = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCfdis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppConciliaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CfdiId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MovimientoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Puntaje = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConciliaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppEstadosDeCuenta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Banco = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Cuenta = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Diferencia = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodoFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodoInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SaldoFinal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SaldoInicial = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SaltosDeSaldo = table.Column<int>(type: "integer", nullable: false),
                    TotalAbonos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCargos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEstadosDeCuenta", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppMovimientosBancarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Abono = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Cargo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Concepto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EstadoDeCuentaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Saldo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SinFacturaPorque = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMovimientosBancarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppMovimientosBancarios_AppEstadosDeCuenta_EstadoDeCuentaId",
                        column: x => x.EstadoDeCuentaId,
                        principalTable: "AppEstadosDeCuenta",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCfdis_DocumentoId",
                table: "AppCfdis",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCfdis_OrganizacionId_Fecha",
                table: "AppCfdis",
                columns: new[] { "OrganizacionId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCfdis_OrganizacionId_Uuid",
                table: "AppCfdis",
                columns: new[] { "OrganizacionId", "Uuid" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AppConciliaciones_CfdiVivo",
                table: "AppConciliaciones",
                column: "CfdiId",
                unique: true,
                filter: "\"Estado\" <> 2 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AppConciliaciones_MovimientoVivo",
                table: "AppConciliaciones",
                column: "MovimientoId",
                unique: true,
                filter: "\"Estado\" <> 2 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AppConciliaciones_OrganizacionId",
                table: "AppConciliaciones",
                column: "OrganizacionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEstadosDeCuenta_DocumentoId",
                table: "AppEstadosDeCuenta",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEstadosDeCuenta_OrganizacionId",
                table: "AppEstadosDeCuenta",
                column: "OrganizacionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppMovimientosBancarios_EstadoDeCuentaId",
                table: "AppMovimientosBancarios",
                column: "EstadoDeCuentaId");

            migrationBuilder.CreateIndex(
                name: "IX_AppMovimientosBancarios_OrganizacionId_Fecha",
                table: "AppMovimientosBancarios",
                columns: new[] { "OrganizacionId", "Fecha" });
        }
    }
}
