using PapaPersonas.Core.Import;

namespace PapaPersonas.Core.Import.Paso2;

/// <summary>
/// Streaming abstraction for Sergio return XLSX reading.
///
/// IMPORTANT: callers rely on streaming semantics to avoid holding complete workbooks in memory.
/// Implementations must not eagerly materialize all rows.
/// </summary>
public interface ISergioRowSource
{
    IReadOnlyList<string> ReadHeaders(string filePath);

    IEnumerable<SergioRowRecord> ReadRows(string filePath, HeaderValidationResult headerValidation);
}
