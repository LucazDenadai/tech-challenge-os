using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompensacaoSaga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelamentoSolicitado",
                table: "SagasOrdemServico",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CausaManual",
                table: "SagasOrdemServico",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstadoRetomada",
                table: "SagasOrdemServico",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EstornoConfirmado",
                table: "SagasOrdemServico",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EstornoPendente",
                table: "SagasOrdemServico",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EtapaIniciadaEm",
                table: "SagasOrdemServico",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "LiberacaoConfirmada",
                table: "SagasOrdemServico",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PagamentoAprovado",
                table: "SagasOrdemServico",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Reenvios",
                table: "SagasOrdemServico",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelamentoSolicitado",
                table: "SagasOrdemServico");

            migrationBuilder.DropColumn(
                name: "CausaManual",
                table: "SagasOrdemServico");

            migrationBuilder.DropColumn(
                name: "EstadoRetomada",
                table: "SagasOrdemServico");

            migrationBuilder.DropColumn(
                name: "EstornoConfirmado",
                table: "SagasOrdemServico");

            migrationBuilder.DropColumn(
                name: "EstornoPendente",
                table: "SagasOrdemServico");

            migrationBuilder.DropColumn(
                name: "EtapaIniciadaEm",
                table: "SagasOrdemServico");

            migrationBuilder.DropColumn(
                name: "LiberacaoConfirmada",
                table: "SagasOrdemServico");

            migrationBuilder.DropColumn(
                name: "PagamentoAprovado",
                table: "SagasOrdemServico");

            migrationBuilder.DropColumn(
                name: "Reenvios",
                table: "SagasOrdemServico");
        }
    }
}
