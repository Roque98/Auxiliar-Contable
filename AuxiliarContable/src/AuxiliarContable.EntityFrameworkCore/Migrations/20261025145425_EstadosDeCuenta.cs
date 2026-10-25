using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class EstadosDeCuenta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEstadosDeCuenta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Banco = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Cuenta = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PeriodoInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodoFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SaldoInicial = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SaldoFinal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalAbonos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCargos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Diferencia = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SaltosDeSaldo = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    EstadoDeCuentaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Concepto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cargo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Abono = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Saldo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMovimientosBancarios");

            migrationBuilder.DropTable(
                name: "AppEstadosDeCuenta");
        }
    }
}
