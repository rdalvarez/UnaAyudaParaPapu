namespace PapaPersonas.Core.Query.Paso3;

public interface IPaso3ExportService
{
    Task<Paso3ExportResult> ExportCsvAsync(
        Paso3ExportRequest request,
        IProgress<Paso3ExportProgress>? progress,
        CancellationToken cancellationToken);
}
