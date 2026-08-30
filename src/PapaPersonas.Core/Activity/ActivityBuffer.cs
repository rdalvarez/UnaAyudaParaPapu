using System.Collections.ObjectModel;

namespace PapaPersonas.Core.Activity;

public sealed class ActivityBuffer
{
    private readonly int _maxMessages;
    private readonly Queue<ActivityEntry> _entries;

    public ActivityBuffer(int maxMessages = 500)
    {
        if (maxMessages < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages), "La cantidad máxima de mensajes debe ser al menos 1.");
        }

        _maxMessages = maxMessages;
        _entries = new Queue<ActivityEntry>(Math.Min(maxMessages, 64));
    }

    public int Count => _entries.Count;

    public void Add(ActivitySeverity severity, string phase, string message, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            throw new ArgumentException("Se requiere la etapa.", nameof(phase));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Se requiere el mensaje.", nameof(message));
        }

        var entry = new ActivityEntry(
            timestamp ?? DateTimeOffset.Now,
            severity,
            phase.Trim(),
            message.Trim());

        _entries.Enqueue(entry);
        while (_entries.Count > _maxMessages)
        {
            _entries.Dequeue();
        }
    }

    public void Clear()
    {
        _entries.Clear();
    }

    public IReadOnlyList<ActivityEntry> SnapshotChronological()
    {
        return _entries.ToArray();
    }

    public ReadOnlyCollection<ActivityEntry> SnapshotReadOnly()
    {
        return Array.AsReadOnly(_entries.ToArray());
    }
}
