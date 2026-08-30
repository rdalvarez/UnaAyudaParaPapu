namespace PapaPersonas.Core.App;

public sealed class ProcessBusyCoordinator
{
    private readonly object _sync = new();
    private string? _currentOwner;

    public event EventHandler? BusyStateChanged;

    // Today UI actions are dispatcher-serialized, but this coordinator also protects
    // future timer/background callers that may contend across threads.
    public bool IsBusy
    {
        get
        {
            lock (_sync)
            {
                return !string.IsNullOrWhiteSpace(_currentOwner);
            }
        }
    }

    /// <summary>Intenta reservar la única operación global y notifica cuando pasa a estado ocupado.</summary>
    public bool TryBegin(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("Se requiere el propietario.", nameof(owner));
        }

        var becameBusy = false;
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(_currentOwner))
            {
                return false;
            }

            _currentOwner = owner;
            becameBusy = true;
        }

        if (becameBusy)
        {
            BusyStateChanged?.Invoke(this, EventArgs.Empty);
        }

        return becameBusy;
    }

    /// <summary>Libera la operación sólo si el propietario coincide y notifica cuando queda disponible.</summary>
    public void End(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return;
        }

        var becameIdle = false;
        lock (_sync)
        {
            if (!string.Equals(_currentOwner, owner, StringComparison.Ordinal))
            {
                return;
            }

            _currentOwner = null;
            becameIdle = true;
        }

        if (becameIdle)
        {
            BusyStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
