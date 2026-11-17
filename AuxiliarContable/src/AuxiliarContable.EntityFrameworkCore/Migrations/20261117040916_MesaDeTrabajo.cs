using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class MesaDeTrabajo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaveDeMovimiento",
                table: "AppOperaciones",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Analisis",
                table: "AppDocumentos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AnalizadoUtc",
                table: "AppDocumentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppOperaciones_OrganizacionId_ClaveDeMovimiento",
                table: "AppOperaciones",
                columns: new[] { "OrganizacionId", "ClaveDeMovimiento" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppOperaciones_OrganizacionId_ClaveDeMovimiento",
                table: "AppOperaciones");

            migrationBuilder.DropColumn(
                name: "ClaveDeMovimiento",
                table: "AppOperaciones");

            migrationBuilder.DropColumn(
                name: "Analisis",
                table: "AppDocumentos");

            migrationBuilder.DropColumn(
                name: "AnalizadoUtc",
                table: "AppDocumentos");
        }
    }
}
