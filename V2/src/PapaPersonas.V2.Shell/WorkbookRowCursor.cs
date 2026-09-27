using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace PapaPersonas.V2.Shell;

public interface IWorkbookRowCursor : IDisposable
{
    IReadOnlyList<string?> Headers { get; }
    IReadOnlyList<object?> CurrentRow { get; }
    bool Read();
}

public interface IWorkbookRowCursorFactory
{
    IWorkbookRowCursor Open(string inputPath);
}

public sealed class ExcelDataReaderWorkbookRowCursorFactory : IWorkbookRowCursorFactory
{
    public IWorkbookRowCursor Open(string inputPath) => new ExcelDataReaderWorkbookRowCursor(inputPath);
}

public sealed class ExcelDataReaderWorkbookRowCursor : IWorkbookRowCursor
{
    private readonly Stream stream;
    private readonly IExcelDataReader reader;
    private bool disposed;

    public ExcelDataReaderWorkbookRowCursor(string inputPath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        stream = File.Open(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        reader = ExcelReaderFactory.CreateReader(stream, HeaderReaderConfiguration.Create());

        Headers = reader.Read() ? ReadHeaders() : Array.Empty<string?>();
    }

    public IReadOnlyList<string?> Headers { get; }

    public IReadOnlyList<object?> CurrentRow { get; private set; } = Array.Empty<object?>();

    public bool Read()
    {
        if (!reader.Read())
        {
            CurrentRow = Array.Empty<object?>();
            return false;
        }

        var row = new object?[reader.FieldCount];
        for (var index = 0; index < row.Length; index++)
            row[index] = reader.GetValue(index);

        CurrentRow = row;
        return true;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        reader.Dispose();
        stream.Dispose();
    }

    private IReadOnlyList<string?> ReadHeaders()
    {
        var headers = new string?[reader.FieldCount];
        for (var index = 0; index < headers.Length; index++)
            headers[index] = Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);

        return headers;
    }
}
