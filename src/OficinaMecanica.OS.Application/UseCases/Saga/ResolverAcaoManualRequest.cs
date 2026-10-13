using System.ComponentModel.DataAnnotations;
using OficinaMecanica.OS.Domain.Saga;

namespace OficinaMecanica.OS.Application.UseCases.Saga;

public record ResolverAcaoManualRequest(
    [EnumDataType(typeof(AcaoResolucao))] AcaoResolucao Acao,
    [Required(AllowEmptyStrings = false), StringLength(400)] string Motivo);
