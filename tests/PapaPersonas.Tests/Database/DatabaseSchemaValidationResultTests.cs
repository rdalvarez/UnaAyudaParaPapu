using PapaPersonas.Core.Database;

namespace PapaPersonas.Tests.Database;

public sealed class DatabaseSchemaValidationResultTests
{
    [Fact]
    public void Success_UsesDatabaseSchemaMessage()
    {
        var result = DatabaseSchemaValidationResult.Success();

        Assert.Equal("El esquema de la base es válido.", result.Message);
    }

    [Fact]
    public void Failure_WithTwoIssues_UsesSingularAdditionalIssueMessage()
    {
        var result = DatabaseSchemaValidationResult.Failure(
        [
            CreateIssue("Falta la tabla obligatoria 'personas'."),
            CreateIssue("Falta la tabla obligatoria 'import_runs'.")
        ]);

        Assert.Equal("Falta la tabla obligatoria 'personas'. (y 1 problema adicional).", result.Message);
    }

    [Fact]
    public void Failure_WithThreeIssues_UsesPluralAdditionalIssuesMessage()
    {
        var result = DatabaseSchemaValidationResult.Failure(
        [
            CreateIssue("Falta la tabla obligatoria 'personas'."),
            CreateIssue("Falta la tabla obligatoria 'import_runs'."),
            CreateIssue("Falta la tabla obligatoria 'personas_staging'.")
        ]);

        Assert.Equal("Falta la tabla obligatoria 'personas'. (y 2 problemas adicionales).", result.Message);
    }

    private static DatabaseSchemaValidationIssue CreateIssue(string message) =>
        new(DatabaseSchemaValidationCode.MissingTable, "test", message);
}
