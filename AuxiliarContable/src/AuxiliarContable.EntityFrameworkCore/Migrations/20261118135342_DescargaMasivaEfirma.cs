using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class DescargaMasivaEfirma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSolicitudesDeDescargaMasiva",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anio = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    Emitidas = table.Column<bool>(type: "boolean", nullable: false),
                    Desde = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Hasta = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Automatica = table.Column<bool>(type: "boolean", nullable: false),
                    IdSolicitud = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Mensaje = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NumeroCfdis = table.Column<int>(type: "integer", nullable: false),
                    Paquetes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PaquetesBajados = table.Column<int>(type: "integer", nullable: false),
                    Nuevas = table.Column<int>(type: "integer", nullable: false),
                    Repetidas = table.Column<int>(type: "integer", nullable: false),
                    Rechazadas = table.Column<int>(type: "integer", nullable: false),
                    SolicitadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevisadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminadaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSolicitudesDeDescargaMasiva", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSolicitudesDeDescargaMasiva_Estado_RevisadaUtc",
                table: "AppSolicitudesDeDescargaMasiva",
                columns: new[] { "Estado", "RevisadaUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSolicitudesDeDescargaMasiva_OrganizacionId_Anio_Mes",
                table: "AppSolicitudesDeDescargaMasiva",
                columns: new[] { "OrganizacionId", "Anio", "Mes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSolicitudesDeDescargaMasiva");
        }
    }
}
