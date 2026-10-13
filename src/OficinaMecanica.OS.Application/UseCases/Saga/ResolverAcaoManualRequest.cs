using System.ComponentModel.DataAnnotations;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Application.UseCases.Saga;

public record ResolverAcaoManualRequest(
    [property: EnumDataType(typeof(AcaoResolucao))] AcaoResolucao Acao,
    [property: Required(AllowEmptyStrings = false), StringLength(400)] string Motivo);
