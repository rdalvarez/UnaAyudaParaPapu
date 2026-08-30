using System.Globalization;
using DuckDB.NET.Data;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Query.Paso3;
using PapaPersonas.Infrastructure.Csv;

namespace PapaPersonas.Infrastructure.Query.Paso3;

public sealed class DuckDbPaso3ExportService : IPaso3ExportService
{
    /// <summary>Exporta todas las filas de la consulta a un CSV con escritura temporal y cancelación controlada.</summary>
    public async Task<Paso3ExportResult> ExportCsvAsync(
        Paso3ExportRequest request,
        IProgress<Paso3ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rowsWritten = 0;

        try
        {
            var destinationPath = request.DestinationCsvPath;
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Se requiere la ruta del CSV de destino.", nameof(request));
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new ArgumentException("La ruta del CSV de destino debe incluir una carpeta.", nameof(request));
            }

            var built = Paso3SqlBuilder.Build(request.Query, includePagination: false);

            using var connection = DuckDbPaso3QueryService.OpenConnection(request.Query.DatabasePath);
            rowsWritten = await CsvExportFileWriter.WriteCsvAsync(
                destinationPath,
                built.SelectedColumns,
                async (writer, token) =>
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = built.PreviewSql;
                    DuckDbPaso3QueryService.AddParameters(command, built.Parameters);

                    using var reader = command.ExecuteReader();
                    var localRows = 0;
                    while (reader.Read())
                    {
                        token.ThrowIfCancellationRequested();

                        var rowValues = new string[reader.FieldCount];
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            rowValues[i] = CsvExportFileWriter.FormatValue(reader.GetValue(i));
                        }

                        await CsvExportFileWriter.WriteRowAsync(writer, rowValues, token);

                        localRows++;
                        if (localRows == 1 || localRows % 1000 == 0)
                        {
                            progress?.Report(new Paso3ExportProgress(localRows, $"Progreso de exportación: {localRows} filas escritas."));
                        }
                    }

                    return localRows;
                },
                cancellationToken);

            progress?.Report(new Paso3ExportProgress(rowsWritten, "Exportación completada."));
            return Paso3ExportResult.Completed(rowsWritten, destinationPath);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new Paso3ExportProgress(rowsWritten, "Exportación cancelada."));
            return Paso3ExportResult.Canceled(rowsWritten);
        }
        catch (Exception ex)
        {
            return Paso3ExportResult.Failed(
                rowsWritten,
                UserFacingExceptionMessage.WithTechnicalDetail(
                    "No se pudo completar la exportación. Verificá la ruta de destino y volvé a intentar.",
                    ex));
        }
    }
}
