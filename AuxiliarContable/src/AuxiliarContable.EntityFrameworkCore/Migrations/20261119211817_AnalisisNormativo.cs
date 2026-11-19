using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class AnalisisNormativo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAnalisisNormativos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Pregunta = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FechaDeLaOperacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContraparteTipoDePersona = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ContraparteResidencia = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Disparadores = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    PedidoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: true),
                    Conclusion = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    Confianza = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ListoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAnalisisNormativos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAnalisisNormativos_Estado_PedidoUtc",
                table: "AppAnalisisNormativos",
                columns: new[] { "Estado", "PedidoUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAnalisisNormativos_OrganizacionId_CreationTime",
                table: "AppAnalisisNormativos",
                columns: new[] { "OrganizacionId", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAnalisisNormativos");
        }
    }
}
