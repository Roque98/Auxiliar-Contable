using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class Solicitudes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSolicitudes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sentido = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Texto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OperacionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Respuesta = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AtendidaPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtendidaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSolicitudes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSolicitudes_OrganizacionId_Estado",
                table: "AppSolicitudes",
                columns: new[] { "OrganizacionId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSolicitudes_Sentido_Estado",
                table: "AppSolicitudes",
                columns: new[] { "Sentido", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSolicitudes");
        }
    }
}
