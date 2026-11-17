#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WG.Lattice;
using Content.Shared._WG.Lattice;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.Mech.EntitySystems;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Lattice;

/// <summary>Drift reaches people riding in a mech or sitting in a vehicle's interior, as it does anyone aboard.</summary>
[TestFixture]
public sealed class WGDriftRidersTest
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task MechPilotFeelsDrift(bool onShip)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var players = server.ResolveDependency<IPlayerManager>();

        EntityUid pilot = default;
        await server.WaitAssertion(() =>
        {
            var transform = entities.System<SharedTransformSystem>();
            entities.System<WGLatticeSystem>().Setup(map.MapUid);
            transform.SetWorldPosition(map.Grid.Owner, new Vector2(72000f, 0f));
            var at = onShip ? map.GridCoords : new EntityCoordinates(map.MapUid, new Vector2(72100f, 0f));
            var mech = entities.SpawnEntity("MechRipley", at);
            pilot = entities.SpawnEntity("MobHuman", at);
            players.SetAttachedEntity(players.Sessions.Single(), pilot, force: true);
            Assert.That(entities.System<SharedMechSystem>().TryInsert(mech, pilot), Is.True);

            // Aboard, the ship carries the Drift; in open space the pilot does.
            entities.EnsureComponent<WGDriftComponent>(onShip ? map.Grid.Owner : pilot).Drift = 60f;
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent<WGDriftComponent>(pilot, out var drift) && drift.Felt > 0f, Is.True,
                $"a mech pilot {(onShip ? "aboard a ship" : "in open space")} felt no Drift.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VehicleCrewFeelsDrift()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var players = server.ResolveDependency<IPlayerManager>();

        EntityUid crew = default;
        await server.WaitAssertion(() =>
        {
            var transform = entities.System<SharedTransformSystem>();
            entities.System<WGLatticeSystem>().Setup(map.MapUid);
            transform.SetWorldPosition(map.Grid.Owner, new Vector2(72000f, 0f));
            entities.EnsureComponent<WGDriftComponent>(map.Grid).Drift = 60f;

            // The vehicle stands on the ship; its crew sit on its interior map.
            var vehicle = entities.SpawnEntity("CrateGenericSteel", map.GridCoords);
            var interior = entities.System<SharedMapSystem>().CreateMap(out var interiorId);
#pragma warning disable RA0002 // Link a bare interior map without loading a whole vehicle.
            entities.AddComponent<VehicleInteriorLinkComponent>(interior).Vehicle = vehicle;
#pragma warning restore RA0002
            crew = entities.SpawnEntity("MobHuman", new MapCoordinates(Vector2.Zero, interiorId));
            players.SetAttachedEntity(players.Sessions.Single(), crew, force: true);
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent<WGDriftComponent>(crew, out var drift) && drift.Felt > 0f, Is.True,
                "a vehicle's crew felt none of the Drift of the ship it stands on.");
        });
        await pair.CleanReturnAsync();
    }
}
