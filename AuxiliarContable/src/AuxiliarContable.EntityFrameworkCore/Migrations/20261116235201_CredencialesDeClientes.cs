using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class CredencialesDeClientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppCredencialesDeClientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentimientoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsentimientoPor = table.Column<Guid>(type: "uuid", nullable: true),
                    CerCifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    KeyCifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    ContrasenaDeEfirmaCifrada = table.Column<byte[]>(type: "bytea", nullable: true),
                    EfirmaRfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    NumeroDeCertificado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    VigenteDesdeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VigenteHastaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EfirmaActualizadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CiecRfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    CiecCifrada = table.Column<byte[]>(type: "bytea", nullable: true),
                    CiecActualizadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCredencialesDeClientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppNipsDeUsuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sal = table.Column<byte[]>(type: "bytea", nullable: true),
                    Huella = table.Column<byte[]>(type: "bytea", nullable: true),
                    IntentosFallidos = table.Column<int>(type: "integer", nullable: false),
                    BloqueadoHastaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppNipsDeUsuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppUsosDeCredenciales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Accion = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsosDeCredenciales", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCredencialesDeClientes_OrganizacionId",
                table: "AppCredencialesDeClientes",
                column: "OrganizacionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppNipsDeUsuarios_UsuarioId",
                table: "AppNipsDeUsuarios",
                column: "UsuarioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsosDeCredenciales_OrganizacionId_CreationTime",
                table: "AppUsosDeCredenciales",
                columns: new[] { "OrganizacionId", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCredencialesDeClientes");

            migrationBuilder.DropTable(
                name: "AppNipsDeUsuarios");

            migrationBuilder.DropTable(
                name: "AppUsosDeCredenciales");
        }
    }
}
