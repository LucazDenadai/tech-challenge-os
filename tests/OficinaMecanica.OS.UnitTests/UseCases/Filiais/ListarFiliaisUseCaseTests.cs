using Moq;
using OficinaMecanica.OS.Application.Ports.Out;
using OficinaMecanica.OS.Application.UseCases.Filial;
using DomainFilial = OficinaMecanica.OS.Domain.Entities.Filial;

namespace OficinaMecanica.OS.UnitTests.UseCases.Filiais;

public class ListarFiliaisUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_DevolveSoAtivasOrdenadasPorCodigo()
    {
        var inativa = new DomainFilial("FILIAL-C", "Centro");
        inativa.Desativar();
        var repo = new Mock<IFilialRepository>();
        repo.Setup(r => r.ObterTodosAsync(default))
            .ReturnsAsync([new DomainFilial("FILIAL-B", "Bairro"), inativa, new DomainFilial("FILIAL-A", "Avenida")]);

        var resultado = await new ListarFiliaisUseCase(repo.Object).ExecutarAsync();

        Assert.Equal(["FILIAL-A", "FILIAL-B"], resultado.Select(f => f.Codigo));
    }
}
