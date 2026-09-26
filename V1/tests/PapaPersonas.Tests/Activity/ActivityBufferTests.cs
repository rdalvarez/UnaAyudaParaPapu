using PapaPersonas.Core.Activity;

namespace PapaPersonas.Tests.Activity;

public sealed class ActivityBufferTests
{
    [Fact]
    public void SnapshotChronological_PreservesInsertionOrder()
    {
        var buffer = new ActivityBuffer(maxMessages: 10);
        var t0 = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);

        buffer.Add(ActivitySeverity.Info, "Paso 1", "Started", t0);
        buffer.Add(ActivitySeverity.Warning, "Paso 1", "Validation warning", t0.AddSeconds(1));
        buffer.Add(ActivitySeverity.Success, "Paso 1", "Completed", t0.AddSeconds(2));

        var snapshot = buffer.SnapshotChronological();

        Assert.Equal(3, snapshot.Count);
        Assert.Equal("Started", snapshot[0].Message);
        Assert.Equal("Validation warning", snapshot[1].Message);
        Assert.Equal("Completed", snapshot[2].Message);
    }

    [Fact]
    public void Add_WhenExceedingCap_KeepsOnlyLatestMessages()
    {
        var buffer = new ActivityBuffer(maxMessages: 3);

        buffer.Add(ActivitySeverity.Info, "P2", "1");
        buffer.Add(ActivitySeverity.Info, "P2", "2");
        buffer.Add(ActivitySeverity.Info, "P2", "3");
        buffer.Add(ActivitySeverity.Info, "P2", "4");

        var snapshot = buffer.SnapshotChronological();

        Assert.Equal(3, snapshot.Count);
        Assert.Equal(["2", "3", "4"], snapshot.Select(x => x.Message).ToArray());
    }

    [Fact]
    public void Clear_OnNewRun_RemovesPreviousMessages()
    {
        var buffer = new ActivityBuffer(maxMessages: 5);
        buffer.Add(ActivitySeverity.Info, "P1", "before-clear");

        buffer.Clear();
        buffer.Add(ActivitySeverity.Info, "P1", "after-clear");

        var snapshot = buffer.SnapshotChronological();
        Assert.Single(snapshot);
        Assert.Equal("after-clear", snapshot[0].Message);
    }
}
