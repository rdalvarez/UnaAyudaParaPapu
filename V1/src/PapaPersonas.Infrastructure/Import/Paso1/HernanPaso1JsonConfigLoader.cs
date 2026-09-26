using System.Text.Json;
using PapaPersonas.Core.Import;
using PapaPersonas.Core.Import.Paso1;

namespace PapaPersonas.Infrastructure.Import.Paso1;

public static class HernanPaso1JsonConfigLoader
{
    /// <summary>Lee la configuración JSON, valida sus mapeos y devuelve un resultado de carga explícito.</summary>
    public static (bool IsSuccess, HernanPaso1Config? Config, string? Error) LoadFromFile(string configPath)
    {
        try
        {
            var json = File.ReadAllText(configPath);
            var dto = JsonSerializer.Deserialize<Paso1HernanConfigDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto?.OutputColumns is null)
            {
                return (false, null, "La configuración debe contener 'outputColumns'.");
            }

            var mappings = dto.OutputColumns
                .Select(x => new HernanOutputColumnMapping(x.OutputName ?? string.Empty, x.SourceName ?? string.Empty))
                .ToArray();

            var config = new HernanPaso1Config(mappings);
            var validation = HernanConfigValidator.Validate(config);
            if (!validation.IsValid)
            {
                return (false, null, string.Join("; ", validation.Errors));
            }

            return (true, config, null);
        }
        catch (JsonException ex)
        {
            return (false, null, UserFacingExceptionMessage.WithTechnicalDetail("El formato JSON de la configuración no es válido.", ex));
        }
        catch (IOException ex)
        {
            return (false, null, UserFacingExceptionMessage.WithTechnicalDetail("No se pudo leer el archivo de configuración.", ex));
        }
    }

    private sealed record Paso1HernanConfigDto(IReadOnlyList<OutputColumnDto> OutputColumns);

    private sealed record OutputColumnDto(string? OutputName, string? SourceName);
}
