namespace PapaPersonas.Core.Import.Paso1;

public sealed record HernanPaso1Config(IReadOnlyList<HernanOutputColumnMapping> OutputColumns)
{
    public static HernanPaso1Config Default { get; } = new(
    [
        new("CUIL", "CUIL"),
        new("APELLIDO_NOMBRE", "CUIL_APENOM"),
        new("CD_OS", "CUIL_CODOS"),
        new("DESCRIPCION O_S", "CUIL_DESCRIPOS"),
        new("FECHA_NAC", "CUIL_FECHANAC"),
        new("EDAD", "CUIL_EDAD")
    ]);
}
