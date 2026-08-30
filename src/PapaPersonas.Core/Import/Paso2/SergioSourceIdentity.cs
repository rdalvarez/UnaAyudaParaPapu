namespace PapaPersonas.Core.Import.Paso2;

public sealed record SergioSourceIdentity(
    string NormalizedAbsolutePath,
    string ContentHash,
    string SourceShapeFingerprint);
