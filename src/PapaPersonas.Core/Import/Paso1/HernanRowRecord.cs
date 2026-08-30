namespace PapaPersonas.Core.Import.Paso1;

public sealed record HernanRowRecord(
    int SourceRowNumber,
    IReadOnlyDictionary<string, string?> CanonicalValues)
{
    public string? RawCuil => GetValue("cuil");

    public string? GetValue(string canonicalField)
    {
        return CanonicalValues.TryGetValue(canonicalField, out var value)
            ? value
            : null;
    }

    public static HernanRowRecord FromFixed(
        int sourceRowNumber,
        string? rawCuil,
        string? apellidoNombre,
        string? codigoObraSocial,
        string? descripcionObraSocial,
        string? fechaNacimiento,
        string? edad)
    {
        return new HernanRowRecord(
            sourceRowNumber,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["cuil"] = rawCuil,
                ["apellido_nombre"] = apellidoNombre,
                ["codigo_obra_social"] = codigoObraSocial,
                ["obra_social"] = descripcionObraSocial,
                ["fecha_nacimiento"] = fechaNacimiento,
                ["edad"] = edad
            });
    }
}
