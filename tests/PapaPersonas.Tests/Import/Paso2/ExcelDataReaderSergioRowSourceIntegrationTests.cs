using System.IO.Compression;
using System.Text;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso2;
using PapaPersonas.Infrastructure.Database;
using PapaPersonas.Infrastructure.Import.Paso2;

namespace PapaPersonas.Tests.Import.Paso2;

public sealed class ExcelDataReaderSergioRowSourceIntegrationTests
{
    [Fact]
    public void ReadRows_SyntheticXlsx_MapsActual28HeadersAndNumericCells()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "sergio_synthetic.xlsx");

        try
        {
            CreateSyntheticXlsx(filePath, HeaderContracts.SergioActualHeaders28);

            var source = new ExcelDataReaderSergioRowSource();
            var headers = source.ReadHeaders(filePath);
            var validation = HeaderContractValidator.Validate(ImportStage.SergioReturn, headers);

            Assert.True(validation.IsValid);
            Assert.Equal("fecha_nacimiento", validation.SourceToCanonical["FECNANAC"]);

            var rows = source.ReadRows(filePath, validation).ToArray();
            Assert.Single(rows);

            var row = rows[0];
            Assert.Equal("20123456789", row.RawCuil);
            Assert.Equal("45567", row.GetValue("fecha_nacimiento"));
            Assert.Equal("33", row.GetValue("edad"));
            Assert.Equal("4412", row.GetValue("codigo_postal"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Analyze_PhysicalXlsxWithRealRowSource_ReadsHeadersAfterSnapshotWriterCloses()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PapaPersonas.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "sergio_synthetic.xlsx");
        var databasePath = Path.Combine(tempRoot, "PapaPersonas.duckdb");

        try
        {
            CreateSyntheticXlsx(filePath, HeaderContracts.SergioActualHeaders28);
            var bootstrap = new DuckDbBootstrapper().Initialize(databasePath);
            Assert.True(bootstrap.IsSuccess, bootstrap.Message);

            var result = new SergioPaso2PreviewProcessor(new ExcelDataReaderSergioRowSource()).Analyze(
                new SergioStagePreviewRequest(filePath, databasePath, new DateOnly(2026, 8, 10)));

            Assert.True(result.IsSuccess, result.FailureMessage);
            Assert.Equal(SergioStagePreviewStatus.ReadyForConfirmation, result.Status);
            Assert.Equal(1, result.Summary.TotalRows);
            Assert.Equal(1, result.Summary.ValidRows);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void CreateSyntheticXlsx(string filePath, IReadOnlyList<string> headers)
    {
        using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);

        WriteEntry(
            zip,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);

        WriteEntry(
            zip,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        WriteEntry(
            zip,
            "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """);

        WriteEntry(
            zip,
            "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);

        var headerCells = string.Join(
            string.Empty,
            headers.Select((h, i) => $"<c r=\"{ColumnName(i + 1)}1\" t=\"inlineStr\"><is><t>{EscapeXml(h)}</t></is></c>"));

        var valueMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CUIL"] = "20123456789",
            ["APELLIDO"] = "SYNTH_LAST",
            ["NOMBRE"] = "SYNTH_FIRST",
            ["DIRECCION"] = "SYNTH_STREET",
            ["CP"] = "4412",
            ["LOCALIDAD"] = "SYNTH_CITY",
            ["PARTIDO"] = "SYNTH_PARTIDO",
            ["PROVINCIA"] = "SYNTH_PROV",
            ["NACIONALIDAD"] = "AR",
            ["TELPART1"] = "1140010001",
            ["TELPART2"] = "1140010002",
            ["CELULAR1"] = "1160010001",
            ["WSP1"] = "1160010001",
            ["CELULAR2"] = "1160010002",
            ["WSP2"] = "1160010002",
            ["CELULAR3"] = "1160010003",
            ["WSP3"] = "1160010003",
            ["CELULAR4"] = "1160010004",
            ["WSP4"] = "1160010004",
            ["CELULAR5"] = "1160010005",
            ["WSP5"] = "1160010005",
            ["EMAIL1"] = "a@b.test",
            ["EMAIL2"] = "c@d.test",
            ["EMAIL3"] = "e@f.test",
            ["CODIGOOS"] = "120",
            ["OBRASOCIAL"] = "SYNTH_OS",
            ["FECNANAC"] = "45567",
            ["EDAD"] = "33"
        };

        var dataCells = string.Join(
            string.Empty,
            headers.Select((h, i) =>
            {
                var reference = $"{ColumnName(i + 1)}2";
                var value = valueMap.TryGetValue(h, out var mapped) ? mapped : string.Empty;
                var numericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "CUIL", "CP", "FECNANAC", "EDAD"
                };

                return numericColumns.Contains(h)
                    ? $"<c r=\"{reference}\"><v>{value}</v></c>"
                    : $"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{EscapeXml(value)}</t></is></c>";
            }));

        WriteEntry(
            zip,
            "xl/worksheets/sheet1.xml",
            $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">{headerCells}</row>
                <row r="2">{dataCells}</row>
              </sheetData>
            </worksheet>
            """);
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static string ColumnName(int columnNumber)
    {
        var dividend = columnNumber;
        var columnName = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = ((char)(65 + modulo)) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }
}
