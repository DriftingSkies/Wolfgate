#nullable enable
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Setting a shuttle's angular thrusters to the state they are already in is skipped, until one is added.</summary>
[TestFixture]
public sealed class WGAngularThrustIdleTest
{
    [Test]
    public async Task RepeatedSetIsSkippedUntilAThrusterArrives()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var thrusters = entMan.System<ThrusterSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var shuttle = entMan.EnsureComponent<ShuttleComponent>(map.Grid.Owner);
            var first = Gyroscope();
            Assert.That(shuttle.AngularThrusters, Does.Contain(first.Owner));
            Assert.That(shuttle.WGAngularFiring, Is.Null, "A new thruster's state is unknown.");

            thrusters.SetAngularThrust(shuttle, true);
            Assert.That(first.Comp.Firing, Is.True);

            // Out of step on purpose: a skipped call leaves the thruster alone.
#pragma warning disable RA0002
            first.Comp.Firing = false;
#pragma warning restore RA0002
            thrusters.SetAngularThrust(shuttle, true);
            Assert.That(first.Comp.Firing, Is.False, "A repeat of the same state doesn't walk the thrusters.");

            var second = Gyroscope();
            Assert.That(shuttle.WGAngularFiring, Is.Null);
            thrusters.SetAngularThrust(shuttle, true);
            Assert.That(first.Comp.Firing, Is.True, "After an arrival every thruster is set again.");
            Assert.That(second.Comp.Firing, Is.True);

            thrusters.SetAngularThrust(shuttle, false);
            Assert.That(first.Comp.Firing, Is.False);
            Assert.That(second.Comp.Firing, Is.False);

            entMan.DeleteEntity(first.Owner);
            entMan.DeleteEntity(second.Owner);
        });

        await pair.CleanReturnAsync();

        Entity<ThrusterComponent> Gyroscope()
        {
            var uid = entMan.SpawnEntity(null, map.GridCoords);
#pragma warning disable RA0002
            var comp = new ThrusterComponent { Type = ThrusterType.Angular };
#pragma warning restore RA0002
            entMan.AddComponent(uid, comp);
            thrusters.EnableThruster(uid, comp);
            return (uid, comp);
        }
    }
}
