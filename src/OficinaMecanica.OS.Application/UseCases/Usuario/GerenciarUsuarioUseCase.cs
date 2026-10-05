using OficinaMecanica.OS.Application.Exceptions;
using OficinaMecanica.OS.Application.Ports.Out;
using DomainUsuario = OficinaMecanica.OS.Domain.Entities.Usuario;

namespace OficinaMecanica.OS.Application.UseCases.Usuario;

public class GerenciarUsuarioUseCase(IUsuarioRepository repository, ITokenService tokenService)
{
    public async Task<IEnumerable<UsuarioResponse>> ObterTodosAsync(CancellationToken ct = default)
    {
        var usuarios = await repository.ObterTodosAsync(ct);
        return usuarios.Where(u => u.Ativo).Select(ToResponse);
    }

    public async Task<UsuarioResponse?> ObterPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var usuario = await repository.ObterPorIdAsync(id, ct);
        return usuario is null ? null : ToResponse(usuario);
    }

    public async Task<IEnumerable<UsuarioResponse>> BuscarPorEmailAsync(string email, CancellationToken ct = default)
    {
        var usuario = await repository.ObterPorEmailAsync(email, ct);
        return usuario is null ? [] : [ToResponse(usuario)];
    }

    public async Task<UsuarioResponse> CriarAsync(CriarUsuarioRequest request, CancellationToken ct = default)
    {
        var existente = await repository.ObterPorEmailAsync(request.Email, ct);
        if (existente is not null)
            throw new InvalidOperationException($"Já existe um usuário com o e-mail '{request.Email}'.");

        var senhaHash = tokenService.HashSenha(request.Senha);
        var usuario = new DomainUsuario(request.Nome, request.Email, senhaHash, request.Perfil);
        await repository.AdicionarAsync(usuario, ct);
        await repository.SalvarAsync(ct);
        return ToResponse(usuario);
    }

    public async Task<UsuarioResponse> AtualizarAsync(Guid id, AtualizarUsuarioRequest request, CancellationToken ct = default)
    {
        var usuario = await repository.ObterPorIdAsync(id, ct)
            ?? throw new NotFoundException("Usuario", id);

        usuario.Atualizar(request.Nome, request.Email, request.Perfil);
        if (request.Senha is not null)
            usuario.AlterarSenha(tokenService.HashSenha(request.Senha));

        await repository.AtualizarAsync(usuario, ct);
        await repository.SalvarAsync(ct);
        return ToResponse(usuario);
    }

    public async Task DesativarAsync(Guid id, CancellationToken ct = default)
    {
        var usuario = await repository.ObterPorIdAsync(id, ct)
            ?? throw new NotFoundException("Usuario", id);

        usuario.Desativar();
        await repository.AtualizarAsync(usuario, ct);
        await repository.SalvarAsync(ct);
    }

    private static UsuarioResponse ToResponse(DomainUsuario u) =>
        new(u.Id, u.Nome, u.Email, u.Perfil, u.Ativo);
}
