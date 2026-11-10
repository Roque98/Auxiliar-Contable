using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class CierreMensual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppConciliacionesBancarias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    Rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    EstadosDeCuenta = table.Column<int>(type: "integer", nullable: false),
                    Facturas = table.Column<int>(type: "integer", nullable: false),
                    Huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: false),
                    ConciliadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConciliacionesBancarias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppPartidasPendientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    Clave = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Resolucion = table.Column<int>(type: "integer", nullable: false),
                    Nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Respuesta = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RespondidaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPartidasPendientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppPeriodosContables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    Cerrado = table.Column<bool>(type: "boolean", nullable: false),
                    CerradoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CerradoPor = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPeriodosContables", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppConciliacionesBancarias_OrganizacionId_Anio_Mes",
                table: "AppConciliacionesBancarias",
                columns: new[] { "OrganizacionId", "Anio", "Mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPartidasPendientes_OrganizacionId_Anio_Mes_Clave",
                table: "AppPartidasPendientes",
                columns: new[] { "OrganizacionId", "Anio", "Mes", "Clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPeriodosContables_OrganizacionId_Anio_Mes",
                table: "AppPeriodosContables",
                columns: new[] { "OrganizacionId", "Anio", "Mes" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConciliacionesBancarias");

            migrationBuilder.DropTable(
                name: "AppPartidasPendientes");

            migrationBuilder.DropTable(
                name: "AppPeriodosContables");
        }
    }
}
