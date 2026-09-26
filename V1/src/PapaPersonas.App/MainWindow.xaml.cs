using System.ComponentModel;
using System.Windows;
using PapaPersonas.App.Ui;
using PapaPersonas.Core.App;
using MessageBox = System.Windows.MessageBox;

namespace PapaPersonas.App;

public partial class MainWindow : Window
{
    private readonly ProcessBusyCoordinator _busyCoordinator = new();
    private readonly MainWindowDatabaseState _databaseState = new();

    public MainWindow()
    {
        InitializeComponent();

        Paso1Control.ConfigureCoordinator(_busyCoordinator);
        Paso2Control.ConfigureCoordinator(_busyCoordinator);
        Paso3Control.ConfigureCoordinator(_busyCoordinator);
        Paso4Control.ConfigureCoordinator(_busyCoordinator);
        MaintenanceControl.ConfigureCoordinator(_busyCoordinator);

        Paso2Control.SetDatabasePathProvider(_databaseState.GetResolvedPath);
        Paso3Control.SetDatabasePathProvider(_databaseState.GetResolvedPath);
        Paso4Control.SetDatabasePathProvider(_databaseState.GetResolvedPath);
        MaintenanceControl.SetDatabasePathProvider(_databaseState.GetConfiguredPath);
    }

    public void SetDatabaseStatus(bool isReady, string databasePath, string message)
    {
        _databaseState.SetReadyStatus(isReady, databasePath);
        MaintenanceControl.SetDatabasePathProvider(_databaseState.GetConfiguredPath);

        if (isReady)
        {
            DbStatusText.Foreground = StatusBrushes.Success;
            DbStatusText.Text = $"DuckDB lista ({databasePath}).";
            return;
        }

        DbStatusText.Foreground = StatusBrushes.Error;
        DbStatusText.Text = $"Falló la inicialización de DuckDB: {message}";
    }

    public void SetInitializingStatus()
    {
        DbStatusText.Foreground = StatusBrushes.Info;
        DbStatusText.Text = "Inicializando DuckDB local...";
    }

    /// <summary>Actualiza el estado compartido de la base y limpia las vistas dependientes después de mantenimiento.</summary>
    public async Task RefreshDatabaseAfterMaintenanceAsync(string databasePath, bool showPendingDialog = false)
    {
        _databaseState.SetReadyStatus(true, databasePath);

        Paso2Control.ClearDatabaseDependentState();
        Paso3Control.ClearDatabaseDependentState();
        await Paso4Control.RefreshDatabaseDependentStateAsync(showPendingDialog);

        DbStatusText.Foreground = StatusBrushes.Success;
        DbStatusText.Text = $"DuckDB lista ({databasePath}).";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busyCoordinator.IsBusy)
        {
            e.Cancel = true;
            MessageBox.Show(
                this,
                "Hay otra operación en curso. Esperá a que termine para cerrar la aplicación.",
                "Operación en curso",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        base.OnClosing(e);
    }
}
