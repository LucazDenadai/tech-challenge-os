using Microsoft.EntityFrameworkCore;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Domain.Entities;
using OficinaMecanica.OS.Domain.Enums;

namespace OficinaMecanica.OS.Infrastructure.Adapters.Out.Persistence.Seed;

// Dados de demonstração sem dados pessoais reais (emenda "Dados da Fase 3 e usuários" do ADR-015).
// Idempotente: cada registro é identificado pela sua chave única e só é inserido se ausente.
public static class SeedDemonstracao
{
    public const string CodigoFilial = "FILIAL-DEMO";
    // Mesmo Id do seed de Operações (emenda "Filiais em Operações" do ADR-015), para os saldos da filial existirem lá.
    public static readonly Guid FilialDemoId = Guid.Parse("378aeb39-37f6-43c1-9526-5b1a9fadd553");

    private static readonly (string Nome, string Email, PerfilUsuario Perfil)[] _usuarios =
    [
        ("Administrador Demo", "admin@oficina.example", PerfilUsuario.Admin),
        ("Atendente Demo", "atendente@oficina.example", PerfilUsuario.Atendente),
        ("Mecânico Demo", "mecanico@oficina.example", PerfilUsuario.Mecanico)
    ];

    // CPFs de exemplo amplamente publicados para testes; e-mails no domínio reservado example.com.
    private static readonly (string Nome, string Cpf, string Email, string Placa, string Marca, string Modelo, int Ano, string Cor)[] _clientes =
    [
        ("Cliente Demo Um", "11144477735", "cliente1@cliente.example", "DEM1A23", "Ford", "Ka", 2019, "Prata"),
        ("Cliente Demo Dois", "52998224725", "cliente2@cliente.example", "DEM2B34", "Fiat", "Argo", 2021, "Branco")
    ];

    public static async Task ExecutarAsync(AppDbContext db, ITokenService tokenService, string senhaUsuarios, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(senhaUsuarios))
            throw new InvalidOperationException("Senha dos usuários de demonstração não configurada (Seed:SenhaUsuarios).");

        if (!await db.Filiais.AnyAsync(f => f.Codigo == CodigoFilial, ct))
            db.Filiais.Add(new Filial(FilialDemoId, CodigoFilial, "Filial Demonstração"));

        foreach (var (nome, email, perfil) in _usuarios)
        {
            if (!await db.Usuarios.AnyAsync(u => u.Email == email, ct))
                db.Usuarios.Add(new Usuario(nome, email, tokenService.HashSenha(senhaUsuarios), perfil));
        }

        foreach (var c in _clientes)
        {
            var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Documento == c.Cpf, ct);
            if (cliente is null)
            {
                cliente = new Cliente(c.Nome, c.Cpf, c.Email, "11900000000", "Endereço de demonstração");
                db.Clientes.Add(cliente);
            }

            if (!await db.Veiculos.AnyAsync(v => v.Placa == c.Placa, ct))
                db.Veiculos.Add(new Veiculo(cliente.Id, c.Placa, c.Marca, c.Modelo, c.Ano, c.Cor));
        }

        await db.SaveChangesAsync(ct);
    }
}
