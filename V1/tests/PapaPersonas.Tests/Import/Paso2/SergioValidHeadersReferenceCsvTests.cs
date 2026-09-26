using System.Text;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso2;

namespace PapaPersonas.Tests.Import.Paso2;

public sealed class SergioValidHeadersReferenceCsvTests
{
    [Fact]
    public void BuildRows_CoversEveryAcceptedHeaderExactlyOnce_WithRuntimeMapSpelling()
    {
        var map = HeaderContracts.GetHeaderToCanonicalMap(ImportStage.SergioReturn);
        var rows = SergioValidHeadersReferenceCsv.BuildRows();

        Assert.Equal(map.Count, rows.Count);
        Assert.Equal(
            rows.Count,
            rows.Select(row => row.EncabezadoValido).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var key in map.Keys)
        {
            var row = Assert.Single(rows, candidate => candidate.EncabezadoValido.Equals(key, StringComparison.Ordinal));
            Assert.Equal(map[key], row.CampoDestino);
        }
    }

    [Fact]
    public void BuildRows_PlacesTemplateHeadersFirstAsPrincipal_ThenAliasesOrdinal()
    {
        var rows = SergioValidHeadersReferenceCsv.BuildRows();
        var template = HeaderContracts.SergioTemplateHeaders36;
        var principals = rows.Take(template.Count).ToArray();
        var aliases = rows.Skip(template.Count).ToArray();

        Assert.Equal(template, principals.Select(row => row.EncabezadoValido));
        Assert.All(principals, row => Assert.Equal("Principal", row.Tipo));
        Assert.All(aliases, row => Assert.Equal("Alias", row.Tipo));
        Assert.Equal(
            aliases.Select(row => row.EncabezadoValido).OrderBy(header => header, StringComparer.Ordinal),
            aliases.Select(row => row.EncabezadoValido));
        Assert.Empty(aliases.Select(row => row.EncabezadoValido).Intersect(template, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildRows_MarksOnlyCuilDestinationAsMandatory()
    {
        var rows = SergioValidHeadersReferenceCsv.BuildRows();

        Assert.Contains(rows, row => row.CampoDestino == "cuil" && row.Obligatoria == "SI");
        Assert.All(
            rows,
            row => Assert.Equal(
                string.Equals(row.CampoDestino, "cuil", StringComparison.Ordinal) ? "SI" : "NO",
                row.Obligatoria));
    }

    [Fact]
    public void FormatCsv_WritesHeaderAndEscapesSpecialCharacters()
    {
        var rows = new SergioValidHeaderReferenceRow[]
        {
            new("A,B", "dest", "Alias", "NO"),
            new("say \"hi\"", "dest", "Alias", "NO"),
            new("plain", "cuil", "Principal", "SI")
        };

        var csv = SergioValidHeadersReferenceCsv.FormatCsv(rows);
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');

        Assert.Equal("ENCABEZADO_VALIDO,CAMPO_DESTINO,TIPO,OBLIGATORIA", lines[0]);
        Assert.Equal("\"A,B\",dest,Alias,NO", lines[1]);
        Assert.Equal("\"say \"\"hi\"\"\",dest,Alias,NO", lines[2]);
        Assert.Equal("plain,cuil,Principal,SI", lines[3]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void FormatCsv_ReferenceRows_AreUtf8SafeAndHaveNoDuplicateAcceptedHeaders()
    {
        var rows = SergioValidHeadersReferenceCsv.BuildRows();
        var csv = SergioValidHeadersReferenceCsv.FormatCsv(rows);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var roundTrip = utf8.GetString(utf8.GetBytes(csv));

        Assert.Equal(csv, roundTrip);
        Assert.StartsWith("ENCABEZADO_VALIDO,CAMPO_DESTINO,TIPO,OBLIGATORIA", csv, StringComparison.Ordinal);

        var dataLines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n').Skip(1).ToArray();
        Assert.Equal(rows.Count, dataLines.Length);
        Assert.Equal(
            dataLines.Length,
            dataLines.Select(line => line.Split(',')[0]).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Write_PublishesUtf8CsvAtomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pp-paso2-ref-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, SergioValidHeadersReferenceCsv.SuggestedFileName);

        try
        {
            SergioValidHeadersReferenceCsv.Write(path);

            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length >= 3);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);

            var text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Assert.Equal(SergioValidHeadersReferenceCsv.FormatCsv(SergioValidHeadersReferenceCsv.BuildRows()), text);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
