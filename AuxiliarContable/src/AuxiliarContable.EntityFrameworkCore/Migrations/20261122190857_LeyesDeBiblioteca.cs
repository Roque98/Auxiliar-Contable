using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class LeyesDeBiblioteca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppLeyesDeClientes_OrganizacionId_DocumentoId",
                table: "AppLeyesDeClientes");

            migrationBuilder.AddColumn<string>(
                name: "Biblioteca",
                table: "AppLeyesDeClientes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLeyesDeClientes_OrganizacionId_Sha256",
                table: "AppLeyesDeClientes",
                columns: new[] { "OrganizacionId", "Sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppLeyesDeClientes_OrganizacionId_Sha256",
                table: "AppLeyesDeClientes");

            migrationBuilder.DropColumn(
                name: "Biblioteca",
                table: "AppLeyesDeClientes");

            migrationBuilder.CreateIndex(
                name: "IX_AppLeyesDeClientes_OrganizacionId_DocumentoId",
                table: "AppLeyesDeClientes",
                columns: new[] { "OrganizacionId", "DocumentoId" },
                unique: true);
        }
    }
}
