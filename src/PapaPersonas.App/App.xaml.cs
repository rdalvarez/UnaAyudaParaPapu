using System.Windows;
using PapaPersonas.Core.Database;
using PapaPersonas.Infrastructure.Database;

namespace PapaPersonas.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>Prepara y valida DuckDB antes de habilitar los flujos principales de la aplicación.</summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        mainWindow.SetInitializingStatus();
        mainWindow.Show();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPathOverride = Environment.GetEnvironmentVariable("PAPAPERSONAS_DB_PATH");
        var resolvedDbPath = AppDatabasePathResolver.Resolve(localAppData, dbPathOverride, Environment.CurrentDirectory);

        var bootstrapper = new DuckDbBootstrapper();
        var result = await Task.Run(() => bootstrapper.Initialize(resolvedDbPath.DatabasePath));

        mainWindow.SetDatabaseStatus(result.IsSuccess, result.DatabasePath, result.Message);
    }
}

