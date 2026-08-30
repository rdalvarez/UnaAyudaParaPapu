namespace PapaPersonas.Core.Import.Paso1;

public sealed record HernanConfigValidationResult(bool IsValid, IReadOnlyList<string> Errors);
