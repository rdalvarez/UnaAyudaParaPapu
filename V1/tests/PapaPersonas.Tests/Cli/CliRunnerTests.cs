using PapaPersonas.Cli;

namespace PapaPersonas.Tests.Cli;

public sealed class CliRunnerTests
{
    private static readonly object ConsoleSync = new();

    [Fact]
    public void Run_Help_ReturnsZero()
    {
        var exitCode = CliRunner.Run(["--help"]);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_UnknownCommand_ReturnsError()
    {
        var exitCode = CliRunner.Run(["unknown-cmd"]);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Run_BadConfigPath_ReturnsExitCode3()
    {
        lock (ConsoleSync)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var outWriter = new StringWriter();
            using var errorWriter = new StringWriter();

            try
            {
                Console.SetOut(outWriter);
                Console.SetError(errorWriter);

                var exitCode = CliRunner.Run(
                [
                    "paso1-hernan",
                    "--input", "synthetic-input.xlsx",
                    "--output", "synthetic-output",
                    "--config", "config/DOES_NOT_EXIST.json"
                ]);

                Assert.Equal(3, exitCode);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }
}
