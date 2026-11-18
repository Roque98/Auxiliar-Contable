using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class ImpuestosAlDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppImpuestosDelMes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: true),
                    CalculadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PorActualizar = table.Column<bool>(type: "boolean", nullable: false),
                    PedidoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Facturas = table.Column<int>(type: "integer", nullable: false),
                    Canceladas = table.Column<int>(type: "integer", nullable: false),
                    Danadas = table.Column<int>(type: "integer", nullable: false),
                    SinEstatus = table.Column<int>(type: "integer", nullable: false),
                    HuellaDelPerfil = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppImpuestosDelMes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppImpuestosDelMes_OrganizacionId_Anio_Mes",
                table: "AppImpuestosDelMes",
                columns: new[] { "OrganizacionId", "Anio", "Mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppImpuestosDelMes_PorActualizar_PedidoUtc",
                table: "AppImpuestosDelMes",
                columns: new[] { "PorActualizar", "PedidoUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppImpuestosDelMes");
        }
    }
}
