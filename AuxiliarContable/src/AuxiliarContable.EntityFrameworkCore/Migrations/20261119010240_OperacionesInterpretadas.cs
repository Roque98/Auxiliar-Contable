using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class OperacionesInterpretadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppOperacionesInterpretadas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    Pedida = table.Column<bool>(type: "boolean", nullable: false),
                    PedidaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Huella = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Resultado = table.Column<string>(type: "text", nullable: true),
                    InterpretadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppOperacionesInterpretadas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppOperacionesInterpretadas_OperacionId",
                table: "AppOperacionesInterpretadas",
                column: "OperacionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppOperacionesInterpretadas_OrganizacionId_Anio_Mes",
                table: "AppOperacionesInterpretadas",
                columns: new[] { "OrganizacionId", "Anio", "Mes" });

            migrationBuilder.CreateIndex(
                name: "IX_AppOperacionesInterpretadas_Pedida_PedidaUtc",
                table: "AppOperacionesInterpretadas",
                columns: new[] { "Pedida", "PedidaUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppOperacionesInterpretadas");
        }
    }
}
