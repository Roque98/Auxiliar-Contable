using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class NominaCreditoInfonavitParaSua : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreditoInfonavitInicio",
                table: "AppTrabajadores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditoInfonavitNumero",
                table: "AppTrabajadores",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreditoInfonavitInicio",
                table: "AppTrabajadores");

            migrationBuilder.DropColumn(
                name: "CreditoInfonavitNumero",
                table: "AppTrabajadores");
        }
    }
}
