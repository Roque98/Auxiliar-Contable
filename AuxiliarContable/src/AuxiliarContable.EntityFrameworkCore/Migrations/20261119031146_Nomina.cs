using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class Nomina : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppPeriodosDeNomina",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Fin = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Periodicidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Dias = table.Column<int>(type: "integer", nullable: false),
                    Incidencias = table.Column<string>(type: "text", nullable: true),
                    Resultado = table.Column<string>(type: "text", nullable: true),
                    CalculadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppPeriodosDeNomina", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppTrabajadores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    Curp = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    Nss = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    FechaIngreso = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaBaja = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SalarioDiario = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalarioDiarioIntegrado = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DiasAguinaldo = table.Column<int>(type: "integer", nullable: false),
                    PrimaVacacionalPct = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    CreditoInfonavitTipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreditoInfonavitValor = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Fonacot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
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
                    table.PrimaryKey("PK_AppTrabajadores", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppPeriodosDeNomina_OrganizacionId_Inicio",
                table: "AppPeriodosDeNomina",
                columns: new[] { "OrganizacionId", "Inicio" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTrabajadores_OrganizacionId",
                table: "AppTrabajadores",
                column: "OrganizacionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppPeriodosDeNomina");

            migrationBuilder.DropTable(
                name: "AppTrabajadores");
        }
    }
}
