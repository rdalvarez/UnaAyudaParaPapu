using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso1;

namespace PapaPersonas.Tests.Import.Paso1;

public sealed class HernanPreparationServiceTests
{
    [Fact]
    public void Process_ValidRows_ProducesSergioCsvRows()
    {
        var fs = new InMemoryFileSystem();
        var input = "input.xlsx";
        var outputDir = "out";

        fs.Headers[input] =
        [
            "CUIL", "CUIL_APENOM", "CUIL_CODOS", "CUIL_DESCRIPOS", "CUIL_FECHANAC", "CUIL_EDAD"
        ];

        fs.Rows[input] =
        [
            HernanRowRecord.FromFixed(2, "20-12345678-9", "SYNTHETIC A", "001", "OS A", "2024-01-10", "30"),
            HernanRowRecord.FromFixed(3, "20999999999", "SYNTHETIC B", "002", "OS B", "2023-05-01", "41")
        ];

        var service = new HernanPreparationService(fs, fs);
        var result = service.Process(new HernanPreparationRequest(input, outputDir, "sergio.csv", "rechazados.csv"));

        Assert.True(result.IsSuccess);
        var sergio = fs.GetFile(Path.Combine(outputDir, "sergio.csv"));
        Assert.Contains("\"CUIL\",\"APELLIDO_NOMBRE\",\"CD_OS\",\"DESCRIPCION O_S\",\"FECHA_NAC\",\"EDAD\"", sergio);
        Assert.Contains("\"20-12345678-9\"", sergio);
        Assert.Contains("\"20999999999\"", sergio);
    }

    [Fact]
    public void Process_MissingAndMalformedCuil_GoToRejected()
    {
        var fs = CreateDefaultFs();
        fs.Rows["input.xlsx"] =
        [
            HernanRowRecord.FromFixed(2, null, "A", "1", "OS", "2024-01-01", "20"),
            HernanRowRecord.FromFixed(3, "20-12345678-A", "B", "1", "OS", "2024-01-01", "20"),
            HernanRowRecord.FromFixed(4, "20123456789", "C", "1", "OS", "2024-01-01", "20")
        ];

        var service = new HernanPreparationService(fs, fs);
        var result = service.Process(new HernanPreparationRequest("input.xlsx", "out", "sergio.csv", "rechazados.csv"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Summary.ValidRows);
        Assert.Equal(2, result.Summary.RejectedRows);

        var rejected = fs.GetFile(Path.Combine("out", "rechazados.csv"));
        Assert.Contains("MissingCuil", rejected);
        Assert.Contains("InvalidCuilCharacters", rejected);
    }

    [Fact]
    public void Process_DuplicateCuil_AllRowsRejected()
    {
        var fs = CreateDefaultFs();
        fs.Rows["input.xlsx"] =
        [
            HernanRowRecord.FromFixed(2, "20-12345678-9", "A", "1", "OS", "2024-01-01", "20"),
            HernanRowRecord.FromFixed(3, "20123456789", "B", "1", "OS", "2024-01-01", "20")
        ];

        var service = new HernanPreparationService(fs, fs);
        var result = service.Process(new HernanPreparationRequest("input.xlsx", "out", "sergio.csv", "rechazados.csv"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Summary.ValidRows);
        Assert.Equal(2, result.Summary.DuplicateCuilRows);

        var sergio = fs.GetFile(Path.Combine("out", "sergio.csv"));
        Assert.DoesNotContain("20-12345678-9", sergio);
        Assert.DoesNotContain("20123456789", sergio);
    }

    [Fact]
    public void Process_MissingRequiredHeader_BlocksProcessing()
    {
        var fs = new InMemoryFileSystem();
        fs.Headers["input.xlsx"] = ["CUIL", "CUIL_APENOM", "CUIL_CODOS", "CUIL_DESCRIPOS", "CUIL_FECHANAC"];
        fs.Rows["input.xlsx"] = [];

        var service = new HernanPreparationService(fs, fs);
        var result = service.Process(new HernanPreparationRequest("input.xlsx", "out", "sergio.csv", "rechazados.csv"));

        Assert.True(result.IsValidationFailure);
        Assert.Contains(result.Errors, e => e.Code == ValidationErrorCode.MissingRequiredHeader && e.Header == "CUIL_EDAD");
    }

    [Fact]
    public void Process_ExtraUnknownHeaders_AreNotices()
    {
        var fs = CreateDefaultFs();
        fs.Headers["input.xlsx"] =
        [
            "CUIL", "CUIL_APENOM", "CUIL_CODOS", "CUIL_DESCRIPOS", "CUIL_FECHANAC", "CUIL_EDAD", "UNKNOWN_EXTRA"
        ];
        fs.Rows["input.xlsx"] = [HernanRowRecord.FromFixed(2, "20123456789", "A", "1", "OS", "2024-01-01", "20")];

        var service = new HernanPreparationService(fs, fs);
        var result = service.Process(new HernanPreparationRequest("input.xlsx", "out", "sergio.csv", "rechazados.csv"));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Notices, n => n.Code == ValidationErrorCode.UnknownHeader);

        var sergio = fs.GetFile(Path.Combine("out", "sergio.csv"));
        Assert.Contains("\"20123456789\"", sergio);
    }

    [Fact]
    public void Process_ConfiguredExtraOutputColumn_IsWrittenWhenSourceExists()
    {
        var fs = CreateDefaultFs();
        fs.Headers["input.xlsx"] =
        [
            "CUIL", "CUIL_APENOM", "CUIL_CODOS", "CUIL_DESCRIPOS", "CUIL_FECHANAC", "CUIL_EDAD", "CUIL_PROVINCIA"
        ];
        fs.Rows["input.xlsx"] =
        [
            new HernanRowRecord(2, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["cuil"] = "20123456789",
                ["apellido_nombre"] = "A",
                ["codigo_obra_social"] = "1",
                ["obra_social"] = "OS",
                ["fecha_nacimiento"] = "2024-01-01",
                ["edad"] = "20",
                ["provincia"] = "SYNTHETIC_PROV"
            })
        ];

        var config = new HernanPaso1Config(
        [
            new("CUIL", "CUIL"),
            new("PROVINCIA", "CUIL_PROVINCIA")
        ]);

        var service = new HernanPreparationService(fs, fs);
        var result = service.Process(new HernanPreparationRequest(
            InputFilePath: "input.xlsx",
            OutputDirectory: "out",
            SergioOutputFileName: "sergio.csv",
            RejectedOutputFileName: "rechazados.csv",
            Config: config));

        Assert.True(result.IsSuccess);
        var sergio = fs.GetFile(Path.Combine("out", "sergio.csv"));
        Assert.Contains("\"CUIL\",\"PROVINCIA\"", sergio);
        Assert.Contains("\"20123456789\",\"SYNTHETIC_PROV\"", sergio);
    }

    [Fact]
    public void Process_ConfiguredUnknownSourceColumn_Blocks()
    {
        var fs = CreateDefaultFs();
        fs.Rows["input.xlsx"] = [HernanRowRecord.FromFixed(2, "20123456789", "A", "1", "OS", "2024-01-01", "20")];

        var config = new HernanPaso1Config(
        [
            new("CUIL", "CUIL"),
            new("EXTRA", "NOT_IN_INPUT")
        ]);

        var service = new HernanPreparationService(fs, fs);
        var result = service.Process(new HernanPreparationRequest(
            InputFilePath: "input.xlsx",
            OutputDirectory: "out",
            SergioOutputFileName: "sergio.csv",
            RejectedOutputFileName: "rechazados.csv",
            Config: config));

        Assert.True(result.IsValidationFailure);
        Assert.Contains(result.Errors, e => e.Message.Contains("NOT_IN_INPUT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Process_DefaultConfig_ProducesDefaultHeaderOrder()
    {
        var fs = CreateDefaultFs();
        fs.Rows["input.xlsx"] = [HernanRowRecord.FromFixed(2, "20123456789", "A", "1", "OS", "2024-01-01", "20")];

        var service = new HernanPreparationService(fs, fs);
        var result = service.Process(new HernanPreparationRequest("input.xlsx", "out", "sergio.csv", "rechazados.csv"));

        Assert.True(result.IsSuccess);
        var sergio = fs.GetFile(Path.Combine("out", "sergio.csv"));
        Assert.Contains("\"CUIL\",\"APELLIDO_NOMBRE\",\"CD_OS\",\"DESCRIPCION O_S\",\"FECHA_NAC\",\"EDAD\"", sergio);
    }

    private static InMemoryFileSystem CreateDefaultFs()
    {
        var fs = new InMemoryFileSystem();
        fs.Headers["input.xlsx"] =
        [
            "CUIL", "CUIL_APENOM", "CUIL_CODOS", "CUIL_DESCRIPOS", "CUIL_FECHANAC", "CUIL_EDAD"
        ];
        return fs;
    }

    private sealed class InMemoryFileSystem : IExcelRowSource, ICsvFileWriter
    {
        public Dictionary<string, IReadOnlyList<string>> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<HernanRowRecord>> Rows { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> ReadHeaders(string filePath)
        {
            return Headers[filePath];
        }

        public IEnumerable<HernanRowRecord> ReadHernanRows(string filePath, IReadOnlyDictionary<string, string> sourceToCanonical)
        {
            return Rows.TryGetValue(filePath, out var rows) ? rows : [];
        }

        public void WritePaso1Outputs(
            string sergioCsvPath,
            string rejectedCsvPath,
            IEnumerable<HernanRowRecord> rows,
            IReadOnlyList<HernanResolvedOutputColumn> outputColumns,
            IReadOnlyDictionary<int, BatchCuilRowResult> rowResults,
            Func<int, bool> includeSergioPredicate,
            Func<int, bool> includeRejectedPredicate)
        {
            using var sergio = new StringWriter();
            using var rejected = new StringWriter();

            sergio.WriteLine("\"CUIL\",\"APELLIDO_NOMBRE\",\"CD_OS\",\"DESCRIPCION O_S\",\"FECHA_NAC\",\"EDAD\"");
            rejected.WriteLine("\"source_row_number\",\"reason_code\",\"raw_cuil\",\"normalized_cuil\",\"message\"");

            if (outputColumns.Count > 0)
            {
                sergio.GetStringBuilder().Clear();
                sergio.WriteLine(string.Join(',', outputColumns.Select(c => $"\"{c.OutputName}\"")));
            }

            foreach (var row in rows)
            {
                if (includeSergioPredicate(row.SourceRowNumber))
                {
                    sergio.WriteLine(string.Join(',', outputColumns.Select(c => $"\"{row.GetValue(c.CanonicalField)}\"")));
                }

                if (!includeRejectedPredicate(row.SourceRowNumber))
                {
                    continue;
                }

                var rr = rowResults[row.SourceRowNumber];
                rejected.WriteLine($"\"{row.SourceRowNumber}\",\"{rr.Issue?.Code}\",\"{row.RawCuil}\",\"{rr.NormalizedCuil}\",\"{rr.Issue?.Message}\"");
            }

            _files[sergioCsvPath] = sergio.ToString();
            _files[rejectedCsvPath] = rejected.ToString();
        }

        public string GetFile(string path)
        {
            return _files[path];
        }
    }
}
