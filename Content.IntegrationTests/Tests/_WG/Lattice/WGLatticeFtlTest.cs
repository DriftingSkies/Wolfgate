using System.Collections.Generic;
using System.Numerics;
using Content.Server._WG.Lattice;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.DroneRings;
using Content.Shared._WG.Lattice;
using Content.Shared._WG.Shuttles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Lattice;

/// <summary>FTL drives die from Untethered space out: no jumping from there, and nothing to jump to there.</summary>
public sealed class WGLatticeFtlTest
{
    [Test]
    public async Task FtlDiesInTheOuterBands()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WGLatticeSystem>();
        var shuttles = entities.System<ShuttleSystem>();
        var transform = entities.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);

            Assert.That(lattice.FtlWorksAt(new MapCoordinates(new Vector2(10000f, 0f), map.MapId)), Is.True, "FTL died in anchored space.");
            Assert.That(lattice.FtlWorksAt(new MapCoordinates(new Vector2(42000f, 0f), map.MapId)), Is.True, "FTL died Adrift.");
            Assert.That(lattice.FtlWorksAt(new MapCoordinates(new Vector2(58000f, 0f), map.MapId)), Is.False, "FTL worked Untethered.");
            Assert.That(lattice.FtlWorksAt(new MapCoordinates(new Vector2(72000f, 0f), map.MapId)), Is.False, "FTL worked in the Deep.");

            transform.SetWorldPosition(map.Grid, new Vector2(58000f, 0f));
            Assert.That(shuttles.CanFTL(map.Grid, out var reason), Is.False, "a ship jumped out of Untethered space.");
            Assert.That(reason, Is.EqualTo(Robust.Shared.Localization.Loc.GetString("wg-lattice-ftl-dead")), "the ship was refused for something other than the Lattice.");

            var drift = entities.EnsureComponent<WGDriftComponent>(map.Grid);
            drift.NoFtl = true;
            Assert.That(shuttles.GetFtlDriveStatus(map.Grid), Is.AnyOf(WGFtlDriveStatus.Missing, WGFtlDriveStatus.Unmoored));
        });

        await pair.CleanReturnAsync();
    }
}
