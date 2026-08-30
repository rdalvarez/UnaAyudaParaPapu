namespace PapaPersonas.Infrastructure.Database;

internal sealed record SchemaMigration(int Version, string Description, string Sql);
