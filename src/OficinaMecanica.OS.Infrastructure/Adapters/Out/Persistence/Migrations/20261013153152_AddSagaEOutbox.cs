using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaEOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMensagens",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Canal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    TraceParent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublicadaEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    UltimoErro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMensagens", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "SagasOrdemServico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OsId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilialId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    VeiculoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    MoedaDiagnostico = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PrecoSnapshotEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuoteVersion = table.Column<int>(type: "integer", nullable: true),
                    ValorOrcamento = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    MoedaOrcamento = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SagasOrdemServico", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SagaItensDiagnostico",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantidade = table.Column<int>(type: "integer", nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    SagaId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SagaItensDiagnostico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SagaItensDiagnostico_SagasOrdemServico_SagaId",
                        column: x => x.SagaId,
                        principalTable: "SagasOrdemServico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SagaTransicoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SagaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstadoAnterior = table.Column<int>(type: "integer", nullable: true),
                    EstadoNovo = table.Column<int>(type: "integer", nullable: false),
                    Causa = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SagaTransicoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SagaTransicoes_SagasOrdemServico_SagaId",
                        column: x => x.SagaId,
                        principalTable: "SagasOrdemServico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMensagens_CorrelationId",
                table: "OutboxMensagens",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMensagens_CriadaEmUtc",
                table: "OutboxMensagens",
                column: "CriadaEmUtc",
                filter: "\"PublicadaEmUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SagaItensDiagnostico_SagaId",
                table: "SagaItensDiagnostico",
                column: "SagaId");

            migrationBuilder.CreateIndex(
                name: "IX_SagasOrdemServico_OsId",
                table: "SagasOrdemServico",
                column: "OsId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SagaTransicoes_SagaId",
                table: "SagaTransicoes",
                column: "SagaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMensagens");

            migrationBuilder.DropTable(
                name: "SagaItensDiagnostico");

            migrationBuilder.DropTable(
                name: "SagaTransicoes");

            migrationBuilder.DropTable(
                name: "SagasOrdemServico");
        }
    }
}
