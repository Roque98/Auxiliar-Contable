using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class ServiciosEspecializados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppContratosEspecializados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ContraparteRfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    ContraparteNombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Objeto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Fin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TrabajadoresAproximados = table.Column<int>(type: "integer", nullable: false),
                    RegistroRepse = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RegistroVigenteHasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Trabajadores = table.Column<string>(type: "text", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppContratosEspecializados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCumplimientosRepse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Periodo = table.Column<int>(type: "integer", nullable: false),
                    Clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCumplimientosRepse", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppContratosEspecializados_OrganizacionId",
                table: "AppContratosEspecializados",
                column: "OrganizacionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCumplimientosRepse_OrganizacionId_Anio_Periodo",
                table: "AppCumplimientosRepse",
                columns: new[] { "OrganizacionId", "Anio", "Periodo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppContratosEspecializados");

            migrationBuilder.DropTable(
                name: "AppCumplimientosRepse");
        }
    }
}
