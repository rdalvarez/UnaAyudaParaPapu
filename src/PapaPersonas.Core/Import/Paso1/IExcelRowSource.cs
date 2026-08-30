namespace PapaPersonas.Core.Import.Paso1;

public interface IExcelRowSource
{
    IReadOnlyList<string> ReadHeaders(string filePath);

    IEnumerable<HernanRowRecord> ReadHernanRows(string filePath, IReadOnlyDictionary<string, string> sourceToCanonical);
}
