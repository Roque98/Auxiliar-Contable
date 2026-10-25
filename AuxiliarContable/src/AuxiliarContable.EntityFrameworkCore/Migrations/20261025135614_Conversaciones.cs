using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class Conversaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppConversaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cerrada = table.Column<bool>(type: "boolean", nullable: false),
                    UltimoMensajeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConversaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppMensajesDeConversaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Rol = table.Column<int>(type: "integer", nullable: false),
                    Texto = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    Bloques = table.Column<string>(type: "text", nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tokens = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMensajesDeConversaciones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppConversaciones_UsuarioId_OrganizacionId_Cerrada",
                table: "AppConversaciones",
                columns: new[] { "UsuarioId", "OrganizacionId", "Cerrada" });

            migrationBuilder.CreateIndex(
                name: "IX_AppMensajesDeConversaciones_ConversacionId_Numero",
                table: "AppMensajesDeConversaciones",
                columns: new[] { "ConversacionId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppMensajesDeConversaciones_CreadoUtc",
                table: "AppMensajesDeConversaciones",
                column: "CreadoUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConversaciones");

            migrationBuilder.DropTable(
                name: "AppMensajesDeConversaciones");
        }
    }
}
