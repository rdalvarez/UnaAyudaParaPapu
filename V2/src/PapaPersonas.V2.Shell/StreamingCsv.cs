using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace PapaPersonas.V2.Shell;

public sealed class StreamingCsvWriter : IDisposable
{
    private readonly StreamWriter writer;

    private StreamingCsvWriter(Stream stream)
    {
        writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1024, leaveOpen: false);
    }

    public static StreamingCsvWriter Open(string path) =>
        new(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None));

    public static StreamingCsvWriter OpenNew(string path) =>
        new(File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None));

    public static StreamingCsvWriter Open(Stream stream) =>
        new(stream);

    public void WriteRow(IEnumerable<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
                writer.Write(',');
            writer.Write(Escape(field ?? string.Empty));
            first = false;
        }
        writer.WriteLine();
    }

    public void Dispose() => writer.Dispose();

    private static string Escape(string value)
    {
        var requiresQuotes = value.Length == 0 || value.Contains(',') ||
            value.Contains('"') ||
            value.Contains('\r') ||
            value.Contains('\n') ||
            value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));
        return requiresQuotes ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}

public sealed class StreamingCsvReader : IDisposable
{
    private readonly Stream stream;
    private readonly IExcelDataReader reader;

    private StreamingCsvReader(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        this.stream = stream;
        reader = ExcelReaderFactory.CreateCsvReader(stream, new ExcelReaderConfiguration
        {
            AutodetectSeparators = new[] { ',' },
            TrimWhiteSpace = false,
            AnalyzeInitialCsvRows = 1
        });
    }

    public static StreamingCsvReader Open(string path) =>
        new(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));

    public IReadOnlyList<string?> CurrentRow { get; private set; } = Array.Empty<string?>();

    public bool Read()
    {
        if (!reader.Read())
        {
            CurrentRow = Array.Empty<string?>();
            return false;
        }

        var row = new string?[reader.FieldCount];
        for (var index = 0; index < row.Length; index++)
            row[index] = Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);
        CurrentRow = row;
        return true;
    }

    public void Dispose()
    {
        reader.Dispose();
        stream.Dispose();
    }
}
