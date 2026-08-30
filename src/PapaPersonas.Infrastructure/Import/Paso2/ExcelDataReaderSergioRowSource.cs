using System.Text;
using ExcelDataReader;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso2;
using PapaPersonas.Infrastructure.Import.Paso1;

namespace PapaPersonas.Infrastructure.Import.Paso2;

/// <summary>
/// ExcelDataReader-based Sergio row source.
///
/// INTENT: keep workbook access streaming so Process 2A can load very large files
/// without materializing all rows in memory.
/// </summary>
public sealed class ExcelDataReaderSergioRowSource : ISergioRowSource
{
    private static bool _encodingProviderRegistered;

    public IReadOnlyList<string> ReadHeaders(string filePath)
    {
        EnsureEncodingProviderRegistered();

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        if (!reader.Read())
        {
            return [];
        }

        var headers = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            headers.Add(CsvEscaper.FormatCellValue(reader.GetValue(i)));
        }

        return headers;
    }

    public IEnumerable<SergioRowRecord> ReadRows(string filePath, HeaderValidationResult headerValidation)
    {
        EnsureEncodingProviderRegistered();

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        if (!reader.Read())
        {
            yield break;
        }

        var sourceIndex = BuildSourceHeaderIndex(reader);
        var canonicalIndex = BuildCanonicalIndex(headerValidation.SourceToCanonical, sourceIndex);

        var sourceRowNumber = 1;
        while (reader.Read())
        {
            sourceRowNumber++;
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var canonical in canonicalIndex.Keys)
            {
                values[canonical] = ReadByCanonical(canonical);
            }

            yield return new SergioRowRecord(sourceRowNumber, values);
        }

        string? ReadByCanonical(string canonical)
        {
            if (!canonicalIndex.TryGetValue(canonical, out var index))
            {
                return null;
            }

            return CsvEscaper.FormatCellValue(reader.GetValue(index));
        }
    }

    private static Dictionary<string, int> BuildSourceHeaderIndex(IExcelDataReader reader)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var sourceHeader = CsvEscaper.FormatCellValue(reader.GetValue(i)).Trim();
            if (string.IsNullOrWhiteSpace(sourceHeader))
            {
                continue;
            }

            if (!index.ContainsKey(sourceHeader))
            {
                index[sourceHeader] = i;
            }
        }

        return index;
    }

    private static Dictionary<string, int> BuildCanonicalIndex(
        IReadOnlyDictionary<string, string> sourceToCanonical,
        IReadOnlyDictionary<string, int> sourceIndex)
    {
        var canonicalIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sourceHeader, canonicalField) in sourceToCanonical)
        {
            if (!sourceIndex.TryGetValue(sourceHeader.Trim(), out var index))
            {
                continue;
            }

            if (!canonicalIndex.ContainsKey(canonicalField))
            {
                canonicalIndex[canonicalField] = index;
            }
        }

        return canonicalIndex;
    }

    private static void EnsureEncodingProviderRegistered()
    {
        if (_encodingProviderRegistered)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encodingProviderRegistered = true;
    }
}
