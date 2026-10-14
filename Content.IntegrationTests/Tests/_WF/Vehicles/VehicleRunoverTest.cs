#nullable enable
using Content.Shared._WF.Vehicles.Core;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>The hold on a mob a vehicle ran over lasts while the mob is under the hull, measured in the hull's own frame.</summary>
[TestFixture]
public sealed class VehicleRunoverTest
{
    /// <summary>
    /// The hull is 0.85 wide, and the mob sits a given distance to its side along the deck. On a deck turned 45 degrees
    /// the hull's bounding box in the world is far bigger than the hull, so a mob lying clear beside it is still inside
    /// that box; it must be let up all the same.
    /// </summary>
    [TestCase(0f, 0.2f, true)]
    [TestCase(0f, 0.9f, false)]
    [TestCase(45f, 0.2f, true)]
    [TestCase(45f, 0.9f, false)]
    [TestCase(30f, 0.9f, false)]
    public async Task ARunOverMobIsHeldOnlyWhileItIsUnderTheHull(float degrees, float sideways, bool held)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, startX: 10.5f, degrees: degrees);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mob = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var timing = server.ResolveDependency<IGameTiming>();
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Grid, 10.5f + sideways, 2.5f));

            var runover = entMan.EnsureComponent<VehicleRunoverComponent>(mob);
            runover.Vehicle = rig.Vehicle;
            runover.Duration = TimeSpan.FromSeconds(1);
            runover.ExpiresAt = timing.CurTime + TimeSpan.FromSeconds(1);
            entMan.Dirty(mob, runover);
        });

        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<VehicleRunoverComponent>(mob), Is.EqualTo(held),
                held ? "A mob under the hull should still be held down." : "A mob clear of the hull should have been let up.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A mob that was run over and has been carried to another map is no longer under the vehicle.</summary>
    [Test]
    public async Task AMobOnAnotherMapIsNotUnderTheHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, startX: 10.5f);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mob = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var timing = server.ResolveDependency<IGameTiming>();
            var xforms = server.System<SharedTransformSystem>();
            var maps = server.System<SharedMapSystem>();

            // The same coordinates as the hull, on a map of its own.
            var other = maps.CreateMap();
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(other, xforms.GetWorldPosition(rig.Vehicle)));

            var runover = entMan.EnsureComponent<VehicleRunoverComponent>(mob);
            runover.Vehicle = rig.Vehicle;
            runover.Duration = TimeSpan.FromSeconds(1);
            runover.ExpiresAt = timing.CurTime + TimeSpan.FromSeconds(1);
            entMan.Dirty(mob, runover);
        });

        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<VehicleRunoverComponent>(mob), Is.False, "A mob on another map was held by the vehicle.");
        });

        await pair.CleanReturnAsync();
    }
}
