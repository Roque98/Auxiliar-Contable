using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class Operaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppOperaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Sentido = table.Column<int>(type: "integer", nullable: false),
                    Importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Descripcion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Contraparte = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FacturaEstado = table.Column<int>(type: "integer", nullable: false),
                    Uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    FacturaDocumentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    SolicitudDeFacturaId = table.Column<Guid>(type: "uuid", nullable: true),
                    FacturaMotivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TransaccionEstado = table.Column<int>(type: "integer", nullable: false),
                    TransaccionForma = table.Column<int>(type: "integer", nullable: true),
                    Referencia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TransaccionDocumentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    MovimientoFueraDelBancoId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransaccionMotivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ControlEstado = table.Column<int>(type: "integer", nullable: false),
                    ControlDocumentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ControlMotivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOperaciones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppOperaciones_OrganizacionId_Fecha",
                table: "AppOperaciones",
                columns: new[] { "OrganizacionId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_AppOperaciones_OrganizacionId_Uuid",
                table: "AppOperaciones",
                columns: new[] { "OrganizacionId", "Uuid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppOperaciones");
        }
    }
}
