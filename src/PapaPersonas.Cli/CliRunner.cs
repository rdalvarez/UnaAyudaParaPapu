using PapaPersonas.Core.Import.Paso1;
using PapaPersonas.Infrastructure.Import.Paso1;

namespace PapaPersonas.Cli;

public static class CliRunner
{
    /// <summary>Interpreta los argumentos, ejecuta Paso 1 y devuelve códigos de salida operativos.</summary>
    public static int Run(string[] args)
    {
        var parse = CliArgumentParser.Parse(args);
        if (parse.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (!parse.IsSuccess)
        {
            Console.Error.WriteLine(parse.Error);
            PrintHelp();
            return 2;
        }

        var options = parse.Options!;

        HernanPaso1Config config;
        if (string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            config = HernanPaso1Config.Default;
        }
        else
        {
            var loaded = HernanPaso1JsonConfigLoader.LoadFromFile(options.ConfigPath);
            if (!loaded.IsSuccess || loaded.Config is null)
            {
                Console.Error.WriteLine(loaded.Error);
                return 3;
            }

            config = loaded.Config;
        }

        var processor = new HernanPaso1Processor();
        var result = processor.Process(new HernanPreparationRequest(
            InputFilePath: options.InputPath,
            OutputDirectory: options.OutputDirectory,
            Config: config));

        Console.WriteLine($"status={(result.IsSuccess ? "success" : (result.IsValidationFailure ? "validation_failure" : "failed"))}");
        Console.WriteLine($"output_sergio_csv={result.SergioCsvPath}");
        Console.WriteLine($"output_rejected_csv={result.RejectedCsvPath}");
        Console.WriteLine($"notices_count={result.Notices.Count}");
        Console.WriteLine($"errors_count={result.Errors.Count}");
        Console.WriteLine($"total_rows={result.Summary.TotalRows}");
        Console.WriteLine($"valid_rows={result.Summary.ValidRows}");
        Console.WriteLine($"rejected_rows={result.Summary.RejectedRows}");
        Console.WriteLine($"missing_cuil_rows={result.Summary.MissingCuilRows}");
        Console.WriteLine($"malformed_cuil_rows={result.Summary.MalformedCuilRows}");
        Console.WriteLine($"duplicate_cuil_rows={result.Summary.DuplicateCuilRows}");

        if (result.IsSuccess)
        {
            return 0;
        }

        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine($"error={error.Code}:{error.Message}");
        }

        return result.IsValidationFailure ? 4 : 5;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine("  dotnet run --project src/PapaPersonas.Cli -- paso1-hernan --input <path.xlsx> --output <folder> [--config config/PARA_HERNAN.json]");
    }
}
