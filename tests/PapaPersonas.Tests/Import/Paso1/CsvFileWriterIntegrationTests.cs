using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso1;
using PapaPersonas.Infrastructure.Import.Paso1;

namespace PapaPersonas.Tests.Import.Paso1;

public sealed class CsvFileWriterIntegrationTests
{
    [Fact]
    public void WritePaso1Outputs_WritesExpectedHeadersOrderAndEscaping()
    {
        var tempDir = CreateUniqueTemporaryDirectory();
        var sergioPath = Path.Combine(tempDir, "sergio.csv");
        var rejectedPath = Path.Combine(tempDir, "rejected.csv");

        try
        {
            var writer = new CsvFileWriter();

            var rows = new[]
            {
                HernanRowRecord.FromFixed(2, "20123456789", "SYNTHETIC, NAME", "01", "OS \"ALPHA\"", "2025-01-31", "30"),
                HernanRowRecord.FromFixed(3, "20-12345678-A", "SYNTHETIC B", "02", "OS B", "2025-02-01", "31")
            };

            var outputColumns = new[]
            {
                new HernanResolvedOutputColumn("CUIL", "cuil"),
                new HernanResolvedOutputColumn("APELLIDO_NOMBRE", "apellido_nombre"),
                new HernanResolvedOutputColumn("CD_OS", "codigo_obra_social"),
                new HernanResolvedOutputColumn("DESCRIPCION O_S", "obra_social"),
                new HernanResolvedOutputColumn("FECHA_NAC", "fecha_nacimiento"),
                new HernanResolvedOutputColumn("EDAD", "edad")
            };

            var rowResults = new Dictionary<int, BatchCuilRowResult>
            {
                [2] = new(2, "20123456789", "20123456789", true, false, null),
                [3] = new(3, "20-12345678-A", null, false, false, new ValidationIssue(
                    ValidationErrorCode.InvalidCuilCharacters,
                    "CUIL contains invalid characters.",
                    SourceRowNumber: 3,
                    RawValue: "20-12345678-A"))
            };

            writer.WritePaso1Outputs(
                sergioPath,
                rejectedPath,
                rows,
                outputColumns,
                rowResults,
                includeSergioPredicate: row => rowResults[row].IsValid,
                includeRejectedPredicate: row => !rowResults[row].IsValid);

            var sergioLines = File.ReadAllLines(sergioPath);
            Assert.Equal("\"CUIL\",\"APELLIDO_NOMBRE\",\"CD_OS\",\"DESCRIPCION O_S\",\"FECHA_NAC\",\"EDAD\"", sergioLines[0]);
            Assert.Equal("\"20123456789\",\"SYNTHETIC, NAME\",\"01\",\"OS \"\"ALPHA\"\"\",\"2025-01-31\",\"30\"", sergioLines[1]);

            var rejectedLines = File.ReadAllLines(rejectedPath);
            Assert.Equal("\"source_row_number\",\"reason_code\",\"raw_cuil\",\"normalized_cuil\",\"message\"", rejectedLines[0]);
            Assert.Contains("\"InvalidCuilCharacters\"", rejectedLines[1]);
            Assert.Contains("\"20-12345678-A\"", rejectedLines[1]);
        }
        finally
        {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    private static string CreateUniqueTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        const int maxAttempts = 5;
        var delay = TimeSpan.FromMilliseconds(50);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(delay);
                delay += delay;
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(delay);
                delay += delay;
            }
        }

        throw new IOException($"Failed to clean test directory after {maxAttempts} attempts: {path}");
    }
}
