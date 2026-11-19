using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class ConciliacionIntegral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppConciliacionesIntegrales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    PedidaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IniciadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Pasos = table.Column<string>(type: "text", nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConciliacionesIntegrales", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppConciliacionesIntegrales_Estado_PedidaUtc",
                table: "AppConciliacionesIntegrales",
                columns: new[] { "Estado", "PedidaUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppConciliacionesIntegrales_OrganizacionId_PedidaUtc",
                table: "AppConciliacionesIntegrales",
                columns: new[] { "OrganizacionId", "PedidaUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConciliacionesIntegrales");
        }
    }
}
