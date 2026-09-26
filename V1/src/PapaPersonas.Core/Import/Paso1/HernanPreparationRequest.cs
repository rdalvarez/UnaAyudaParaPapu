namespace PapaPersonas.Core.Import.Paso1;

public sealed record HernanPreparationRequest(
    string InputFilePath,
    string OutputDirectory,
    string? SergioOutputFileName = null,
    string? RejectedOutputFileName = null,
    HernanPaso1Config? Config = null);
