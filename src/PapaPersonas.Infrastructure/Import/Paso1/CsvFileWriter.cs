using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso1;

namespace PapaPersonas.Infrastructure.Import.Paso1;

public sealed class CsvFileWriter : ICsvFileWriter
{
    /// <summary>Escribe los registros válidos y rechazados en sus CSV correspondientes sin mezclar sus criterios.</summary>
    public void WritePaso1Outputs(
        string sergioCsvPath,
        string rejectedCsvPath,
        IEnumerable<HernanRowRecord> rows,
        IReadOnlyList<HernanResolvedOutputColumn> outputColumns,
        IReadOnlyDictionary<int, BatchCuilRowResult> rowResults,
        Func<int, bool> includeSergioPredicate,
        Func<int, bool> includeRejectedPredicate)
    {
        using var sergioWriter = new StreamWriter(sergioCsvPath, false, System.Text.Encoding.UTF8);
        using var rejectedWriter = new StreamWriter(rejectedCsvPath, false, System.Text.Encoding.UTF8);

        WriteCsvLine(sergioWriter, outputColumns.Select(c => c.OutputName).ToArray());

        WriteCsvLine(
            rejectedWriter,
            "source_row_number",
            "reason_code",
            "raw_cuil",
            "normalized_cuil",
            "message");

        foreach (var row in rows)
        {
            if (includeSergioPredicate(row.SourceRowNumber))
            {
                WriteCsvLine(
                    sergioWriter,
                    outputColumns.Select(c => row.GetValue(c.CanonicalField)).ToArray());
            }

            if (!includeRejectedPredicate(row.SourceRowNumber))
            {
                continue;
            }

            if (!rowResults.TryGetValue(row.SourceRowNumber, out var result))
            {
                continue;
            }

            WriteCsvLine(
                rejectedWriter,
                row.SourceRowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                result.Issue?.Code.ToString() ?? string.Empty,
                row.RawCuil,
                result.NormalizedCuil,
                result.Issue?.Message ?? string.Empty);
        }
    }

    private static void WriteCsvLine(StreamWriter writer, params string?[] values)
    {
        var escaped = values.Select(CsvEscaper.Escape);
        writer.WriteLine(string.Join(',', escaped));
    }
}
