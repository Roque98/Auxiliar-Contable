using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class EfectivoYCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppMovimientosFueraDelBanco",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "date", nullable: false),
                    Importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Concepto = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Contraparte = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Vence = table.Column<DateTime>(type: "date", nullable: true),
                    Saldado = table.Column<DateTime>(type: "date", nullable: true),
                    Nota = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMovimientosFueraDelBanco", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppMovimientosFueraDelBanco_OrganizacionId_Fecha",
                table: "AppMovimientosFueraDelBanco",
                columns: new[] { "OrganizacionId", "Fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMovimientosFueraDelBanco");
        }
    }
}
