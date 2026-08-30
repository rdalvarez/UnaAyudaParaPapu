namespace PapaPersonas.Cli;

public static class CliArgumentParser
{
    /// <summary>Interpreta el comando y sus argumentos, validando los valores mínimos para ejecutarlo.</summary>
    public static (bool IsSuccess, Paso1CommandOptions? Options, string? Error, bool ShowHelp) Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            return (false, null, null, true);
        }

        if (!string.Equals(args[0], "paso1-hernan", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, "Comando desconocido.", false);
        }

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                return (false, null, $"Falta el valor del argumento '{args[i]}'.", false);
            }

            options[args[i]] = args[i + 1];
        }

        if (!options.TryGetValue("--input", out var input) || string.IsNullOrWhiteSpace(input))
        {
            return (false, null, "Falta el argumento obligatorio --input.", false);
        }

        if (!options.TryGetValue("--output", out var output) || string.IsNullOrWhiteSpace(output))
        {
            return (false, null, "Falta el argumento obligatorio --output.", false);
        }

        options.TryGetValue("--config", out var configPath);

        return (true, new Paso1CommandOptions(input, output, configPath), null, false);
    }
}

public sealed record Paso1CommandOptions(string InputPath, string OutputDirectory, string? ConfigPath);
