using PapaPersonas.Cli;

namespace PapaPersonas.Tests.Cli;

public sealed class CliArgumentParserTests
{
    [Fact]
    public void Parse_Paso1Command_Succeeds()
    {
        var parse = CliArgumentParser.Parse(
        [
            "paso1-hernan",
            "--input", "input.xlsx",
            "--output", "out",
            "--config", "config/PARA_HERNAN.json"
        ]);

        Assert.True(parse.IsSuccess);
        Assert.NotNull(parse.Options);
        Assert.Equal("input.xlsx", parse.Options!.InputPath);
        Assert.Equal("out", parse.Options.OutputDirectory);
        Assert.Equal("config/PARA_HERNAN.json", parse.Options.ConfigPath);
    }

    [Fact]
    public void Parse_MissingInput_Fails()
    {
        var parse = CliArgumentParser.Parse(["paso1-hernan", "--output", "out"]);
        Assert.False(parse.IsSuccess);
        Assert.Contains("--input", parse.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
