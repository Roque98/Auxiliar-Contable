using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuxiliarContable.Migrations
{
    /// <inheritdoc />
    public partial class MisLeyes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppHallazgosDeLeyes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tema = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Clave = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Valor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ValorNumerico = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ValorDelSistema = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Condicion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Periodicidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FechaLimite = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Articulo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Etiqueta = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Pagina = table.Column<int>(type: "integer", nullable: true),
                    Cita = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Explicacion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    DecididoPor = table.Column<Guid>(type: "uuid", nullable: true),
                    DecididoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppHallazgosDeLeyes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppLeyesDeClientes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NombreDelArchivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Ambito = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    Municipio = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Materia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Lectura = table.Column<int>(type: "integer", nullable: false),
                    Paginas = table.Column<int>(type: "integer", nullable: true),
                    PaginasConOcr = table.Column<int>(type: "integer", nullable: true),
                    PaginasLeidas = table.Column<int>(type: "integer", nullable: true),
                    Articulos = table.Column<int>(type: "integer", nullable: true),
                    IntentosDeLectura = table.Column<int>(type: "integer", nullable: false),
                    HuellaAnalizada = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HuellaEnCurso = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    ProximoIntentoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnalizadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resumen = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Resultado = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppLeyesDeClientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppPreguntasSobreLeyes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Pregunta = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    PedidoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    Respuesta = table.Column<string>(type: "text", nullable: true),
                    ListaUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPreguntasSobreLeyes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppHallazgosDeLeyes_LeyId",
                table: "AppHallazgosDeLeyes",
                column: "LeyId");

            migrationBuilder.CreateIndex(
                name: "IX_AppHallazgosDeLeyes_OrganizacionId_Decision",
                table: "AppHallazgosDeLeyes",
                columns: new[] { "OrganizacionId", "Decision" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLeyesDeClientes_Lectura_ProximoIntentoUtc",
                table: "AppLeyesDeClientes",
                columns: new[] { "Lectura", "ProximoIntentoUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppLeyesDeClientes_OrganizacionId_DocumentoId",
                table: "AppLeyesDeClientes",
                columns: new[] { "OrganizacionId", "DocumentoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPreguntasSobreLeyes_Estado_PedidoUtc",
                table: "AppPreguntasSobreLeyes",
                columns: new[] { "Estado", "PedidoUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPreguntasSobreLeyes_OrganizacionId_CreationTime",
                table: "AppPreguntasSobreLeyes",
                columns: new[] { "OrganizacionId", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppHallazgosDeLeyes");

            migrationBuilder.DropTable(
                name: "AppLeyesDeClientes");

            migrationBuilder.DropTable(
                name: "AppPreguntasSobreLeyes");
        }
    }
}
