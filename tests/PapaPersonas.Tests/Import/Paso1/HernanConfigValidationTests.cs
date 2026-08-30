using PapaPersonas.Core.Import.Paso1;
using PapaPersonas.Infrastructure.Import.Paso1;

namespace PapaPersonas.Tests.Import.Paso1;

public sealed class HernanConfigValidationTests
{
    [Fact]
    public void DefaultConfig_IsValid()
    {
        var result = HernanConfigValidator.Validate(HernanPaso1Config.Default);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void DuplicateOutputName_FailsValidation()
    {
        var config = new HernanPaso1Config(
        [
            new("CUIL", "CUIL"),
            new("CUIL", "CUIL_APENOM")
        ]);

        var result = HernanConfigValidator.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("No se permite repetir OutputName", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MalformedJson_ReturnsFriendlyFailure()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{ bad-json }");
            var loaded = HernanPaso1JsonConfigLoader.LoadFromFile(tempFile);

            Assert.False(loaded.IsSuccess);
            Assert.NotNull(loaded.Error);
            Assert.Contains("formato JSON", loaded.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("El formato JSON de la configuración no es válido. Detalle técnico: ", loaded.Error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void MissingConfigFile_ReturnsTechnicalIoDetail()
    {
        var missingFile = Path.Combine(Path.GetTempPath(), "papa-personas-missing-" + Guid.NewGuid().ToString("N") + ".json");
        var expectedTechnicalMessage = Assert.Throws<FileNotFoundException>(() => File.ReadAllText(missingFile)).Message;

        var loaded = HernanPaso1JsonConfigLoader.LoadFromFile(missingFile);

        Assert.False(loaded.IsSuccess);
        Assert.Equal($"No se pudo leer el archivo de configuración. Detalle técnico: {expectedTechnicalMessage}", loaded.Error);
    }

    [Fact]
    public void JsonLoader_LoadsValidConfig()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
              "outputColumns": [
                { "outputName": "CUIL", "sourceName": "CUIL" },
                { "outputName": "APELLIDO_NOMBRE", "sourceName": "CUIL_APENOM" }
              ]
            }
            """);

            var loaded = HernanPaso1JsonConfigLoader.LoadFromFile(tempFile);

            Assert.True(loaded.IsSuccess);
            Assert.NotNull(loaded.Config);
            Assert.Equal(2, loaded.Config!.OutputColumns.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void EmptyOutputName_FailsValidation()
    {
        var config = new HernanPaso1Config(
        [
            new("", "CUIL"),
            new("CUIL", "CUIL")
        ]);

        var result = HernanConfigValidator.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("OutputName no puede estar vacío", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptySourceName_FailsValidation()
    {
        var config = new HernanPaso1Config(
        [
            new("CUIL", "CUIL"),
            new("APELLIDO_NOMBRE", "")
        ]);

        var result = HernanConfigValidator.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SourceName no puede estar vacío", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingCuilToCuilMapping_FailsValidation()
    {
        var config = new HernanPaso1Config(
        [
            new("CUIL", "CUIL_ALTERNATE")
        ]);

        var result = HernanConfigValidator.Validate(config);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("debe incluir el mapeo", StringComparison.OrdinalIgnoreCase));
    }
}
