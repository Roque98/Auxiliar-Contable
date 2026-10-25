using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class BovedaYCfdi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaDelDocumento",
                table: "AppDocumentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppCfdis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Uuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Version = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TipoDeComprobante = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    Serie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Folio = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EmisorRfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    EmisorNombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReceptorRfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    ReceptorNombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UsoCfdi = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SubTotal = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    Descuento = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    Moneda = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TipoDeCambio = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    MetodoDePago = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    FormaDePago = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    TotalTrasladados = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    TotalRetenidos = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    Concepto = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_AppCfdis", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCfdis_DocumentoId",
                table: "AppCfdis",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCfdis_OrganizacionId_Fecha",
                table: "AppCfdis",
                columns: new[] { "OrganizacionId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCfdis_OrganizacionId_Uuid",
                table: "AppCfdis",
                columns: new[] { "OrganizacionId", "Uuid" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCfdis");

            migrationBuilder.DropColumn(
                name: "FechaDelDocumento",
                table: "AppDocumentos");
        }
    }
}
