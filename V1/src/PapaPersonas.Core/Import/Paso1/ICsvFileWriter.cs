namespace PapaPersonas.Core.Import.Paso1;

public interface ICsvFileWriter
{
    void WritePaso1Outputs(
        string sergioCsvPath,
        string rejectedCsvPath,
        IEnumerable<HernanRowRecord> rows,
        IReadOnlyList<HernanResolvedOutputColumn> outputColumns,
        IReadOnlyDictionary<int, BatchCuilRowResult> rowResults,
        Func<int, bool> includeSergioPredicate,
        Func<int, bool> includeRejectedPredicate);
}
