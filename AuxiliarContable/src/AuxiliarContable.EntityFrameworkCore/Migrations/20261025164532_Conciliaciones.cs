using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class Conciliaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SinFacturaPorque",
                table: "AppMovimientosBancarios",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppConciliaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovimientoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CfdiId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Puntaje = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_AppConciliaciones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppConciliaciones_CfdiVivo",
                table: "AppConciliaciones",
                column: "CfdiId",
                unique: true,
                filter: "\"Estado\" <> 2 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AppConciliaciones_MovimientoVivo",
                table: "AppConciliaciones",
                column: "MovimientoId",
                unique: true,
                filter: "\"Estado\" <> 2 AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AppConciliaciones_OrganizacionId",
                table: "AppConciliaciones",
                column: "OrganizacionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConciliaciones");

            migrationBuilder.DropColumn(
                name: "SinFacturaPorque",
                table: "AppMovimientosBancarios");
        }
    }
}
