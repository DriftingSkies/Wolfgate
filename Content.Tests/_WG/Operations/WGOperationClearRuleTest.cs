using System.Collections.Generic;
using Content.Server._WG.Operations;
using Content.Shared._WG.Operations;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._WG.Operations;

/// <summary>A site clears with every guard the operation brought down and most of those its map spawned.</summary>
[TestFixture]
public sealed class WGOperationClearRuleTest
{
    private static (WGActiveOperationComponent Active, List<EntityUid> Brought, List<EntityUid> Mapped) Site(int brought, int mapped)
    {
        var active = new WGActiveOperationComponent();
        var b = new List<EntityUid>();
        var m = new List<EntityUid>();
        var next = 1;
        for (var i = 0; i < brought; i++, next++)
        {
            var uid = new EntityUid(next);
            active.Npcs.Add(uid);
            active.Brought.Add(uid);
            b.Add(uid);
        }
        for (var i = 0; i < mapped; i++, next++)
        {
            var uid = new EntityUid(next);
            active.Npcs.Add(uid);
            m.Add(uid);
        }
        return (active, b, m);
    }

    [Test]
    public void NoGuardsNeverClears()
    {
        var (active, _, _) = Site(0, 0);
        Assert.That(WGOperationSystem.IsCleared(active, new WGOperationPrototype(), _ => true), Is.False);
    }

    [Test]
    public void EveryBroughtGuardMustBeDown()
    {
        var (active, brought, _) = Site(3, 0);
        var down = new HashSet<EntityUid>(brought);
        down.Remove(brought[0]);
        Assert.That(WGOperationSystem.IsCleared(active, new WGOperationPrototype(), down.Contains), Is.False);
        down.Add(brought[0]);
        Assert.That(WGOperationSystem.IsCleared(active, new WGOperationPrototype(), down.Contains), Is.True);
    }

    [Test]
    public void MostMappedGuardsAreEnough()
    {
        // Ten mapped at the default 0.8: eight down clears, seven doesn't.
        var (active, brought, mapped) = Site(2, 10);
        var down = new HashSet<EntityUid>(brought);
        for (var i = 0; i < 7; i++)
            down.Add(mapped[i]);
        Assert.That(WGOperationSystem.IsCleared(active, new WGOperationPrototype(), down.Contains), Is.False);
        down.Add(mapped[7]);
        Assert.That(WGOperationSystem.IsCleared(active, new WGOperationPrototype(), down.Contains), Is.True);

        // The mapped share never excuses a brought guard.
        down.Remove(brought[1]);
        foreach (var npc in mapped)
            down.Add(npc);
        Assert.That(WGOperationSystem.IsCleared(active, new WGOperationPrototype(), down.Contains), Is.False);
    }
}
