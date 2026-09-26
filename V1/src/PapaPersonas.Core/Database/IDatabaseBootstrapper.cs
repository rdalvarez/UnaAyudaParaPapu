namespace PapaPersonas.Core.Database;

public interface IDatabaseBootstrapper
{
    DatabaseBootstrapResult Initialize(string databaseFilePath);
}
