using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class DescargaDelSat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EstatusSat",
                table: "AppDocumentos",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EstatusSatUtc",
                table: "AppDocumentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Uuid",
                table: "AppDocumentos",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "XmlDanado",
                table: "AppDocumentos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AppMesesSinEntrega",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMesesSinEntrega", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDocumentos_OrganizacionId_Uuid",
                table: "AppDocumentos",
                columns: new[] { "OrganizacionId", "Uuid" });

            migrationBuilder.CreateIndex(
                name: "IX_AppMesesSinEntrega_OrganizacionId_Anio_Mes",
                table: "AppMesesSinEntrega",
                columns: new[] { "OrganizacionId", "Anio", "Mes" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMesesSinEntrega");

            migrationBuilder.DropIndex(
                name: "IX_AppDocumentos_OrganizacionId_Uuid",
                table: "AppDocumentos");

            migrationBuilder.DropColumn(
                name: "EstatusSat",
                table: "AppDocumentos");

            migrationBuilder.DropColumn(
                name: "EstatusSatUtc",
                table: "AppDocumentos");

            migrationBuilder.DropColumn(
                name: "Uuid",
                table: "AppDocumentos");

            migrationBuilder.DropColumn(
                name: "XmlDanado",
                table: "AppDocumentos");
        }
    }
}
