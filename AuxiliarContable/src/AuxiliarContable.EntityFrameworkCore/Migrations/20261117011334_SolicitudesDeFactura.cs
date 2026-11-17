using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class SolicitudesDeFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSolicitudesDeFactura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    ReceptorRfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    ReceptorNombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ReceptorCodigoPostal = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    ReceptorRegimen = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    UsoCfdi = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    FormaDePago = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    MetodoDePago = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Conceptos = table.Column<string>(type: "text", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NotaDelCliente = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Respuesta = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    XmlDocumentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    PdfDocumentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtendidaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AtendidaPor = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSolicitudesDeFactura", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSolicitudesDeFactura_Estado",
                table: "AppSolicitudesDeFactura",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_AppSolicitudesDeFactura_OrganizacionId_Estado",
                table: "AppSolicitudesDeFactura",
                columns: new[] { "OrganizacionId", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSolicitudesDeFactura");
        }
    }
}
