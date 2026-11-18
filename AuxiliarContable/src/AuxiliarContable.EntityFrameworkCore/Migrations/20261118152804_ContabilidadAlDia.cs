using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class ContabilidadAlDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppContabilidadesDeMeses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    PorActualizar = table.Column<bool>(type: "boolean", nullable: false),
                    PedidoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resumen = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppContabilidadesDeMeses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppContabilidadesDeMeses_OrganizacionId_Anio_Mes",
                table: "AppContabilidadesDeMeses",
                columns: new[] { "OrganizacionId", "Anio", "Mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppContabilidadesDeMeses_PorActualizar_PedidoUtc",
                table: "AppContabilidadesDeMeses",
                columns: new[] { "PorActualizar", "PedidoUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppContabilidadesDeMeses");
        }
    }
}
