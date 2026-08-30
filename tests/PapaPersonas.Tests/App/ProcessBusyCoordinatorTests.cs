using PapaPersonas.Core.App;
using System.Collections.Concurrent;

namespace PapaPersonas.Tests.App;

public sealed class ProcessBusyCoordinatorTests
{
    [Fact]
    public void TryBegin_OnlyFirstOwnerCanEnterBusyState()
    {
        var coordinator = new ProcessBusyCoordinator();

        var first = coordinator.TryBegin("Paso1");
        var second = coordinator.TryBegin("Paso2");

        Assert.True(first);
        Assert.False(second);
        Assert.True(coordinator.IsBusy);
    }

    [Fact]
    public void End_WithNonOwner_DoesNotReleaseBusyState()
    {
        var coordinator = new ProcessBusyCoordinator();
        coordinator.TryBegin("Paso1");

        coordinator.End("Paso2");

        Assert.True(coordinator.IsBusy);
    }

    [Fact]
    public void End_WithOwner_ReleasesBusyState()
    {
        var coordinator = new ProcessBusyCoordinator();
        coordinator.TryBegin("Paso2");

        coordinator.End("Paso2");

        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public void End_IsIdempotent_WhenMaintenanceTransfersBeforePendingDecision()
    {
        var coordinator = new ProcessBusyCoordinator();
        var stateChanges = 0;
        coordinator.BusyStateChanged += (_, _) => stateChanges++;

        Assert.True(coordinator.TryBegin("Maintenance"));
        coordinator.End("Maintenance");
        coordinator.End("Maintenance");

        Assert.False(coordinator.IsBusy);
        Assert.Equal(2, stateChanges);
        Assert.True(coordinator.TryBegin("Paso4"));
    }

    [Fact]
    public async Task TryBegin_ConcurrentAcquisition_AllowsExactlyOneOwner_AndWrongOwnerCannotRelease()
    {
        var coordinator = new ProcessBusyCoordinator();
        var owners = Enumerable.Range(1, 24).Select(i => $"Owner-{i}").ToArray();
        var gate = new ManualResetEventSlim(false);
        var results = new ConcurrentBag<(string Owner, bool Acquired)>();

        var tasks = owners.Select(owner => Task.Run(() =>
        {
            gate.Wait();
            var acquired = coordinator.TryBegin(owner);
            results.Add((owner, acquired));
        })).ToArray();

        gate.Set();
        await Task.WhenAll(tasks);

        var winners = results.Where(x => x.Acquired).Select(x => x.Owner).ToArray();
        Assert.Single(winners);

        var winner = winners[0];
        var wrongOwner = owners.First(x => !string.Equals(x, winner, StringComparison.Ordinal));

        coordinator.End(wrongOwner);
        Assert.True(coordinator.IsBusy);

        coordinator.End(winner);
        Assert.False(coordinator.IsBusy);
    }
}
