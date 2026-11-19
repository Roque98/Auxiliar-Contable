using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class NominaMovimientosAfiliatorios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoPostal",
                table: "AppTrabajadores",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroEmpleado",
                table: "AppTrabajadores",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimerApellido",
                table: "AppTrabajadores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SegundoApellido",
                table: "AppTrabajadores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Umf",
                table: "AppTrabajadores",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppMovimientosAfiliatorios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrabajadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SalarioDiario = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalarioDiarioIntegrado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CausaBaja = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    PresentadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMovimientosAfiliatorios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppMovimientosAfiliatorios_OrganizacionId_PresentadoUtc",
                table: "AppMovimientosAfiliatorios",
                columns: new[] { "OrganizacionId", "PresentadoUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMovimientosAfiliatorios");

            migrationBuilder.DropColumn(
                name: "CodigoPostal",
                table: "AppTrabajadores");

            migrationBuilder.DropColumn(
                name: "NumeroEmpleado",
                table: "AppTrabajadores");

            migrationBuilder.DropColumn(
                name: "PrimerApellido",
                table: "AppTrabajadores");

            migrationBuilder.DropColumn(
                name: "SegundoApellido",
                table: "AppTrabajadores");

            migrationBuilder.DropColumn(
                name: "Umf",
                table: "AppTrabajadores");
        }
    }
}
