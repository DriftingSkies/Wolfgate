#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Warheads;
using Content.Server._WF.Warheads.Components;
using Content.Shared._WF.Warheads;
using Content.Shared.Radiation.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Warheads;

/// <summary>
/// Warhead countdowns, disarming, two-person arming, designed warheads and grid deletion.
/// </summary>
[TestFixture]
[TestOf(typeof(WFWarheadSystem))]
public sealed class WFWarheadTest
{
    private const string Chassis = "WFTestWarheadChassis";
    private const string Keyed = "WFTestWarheadKeyed";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {Chassis}
  components:
  - type: Appearance
  - type: WFWarhead

- type: entity
  id: {Keyed}
  components:
  - type: Appearance
  - type: WFWarhead
    payload: WFPayloadTactical
    timer: 30
  - type: WFTwoPersonArming
";

    /// <summary>A payload with every effect but the blast, so the test map survives.</summary>
    private static WFWarheadPayload QuietPayload(bool deleteGrid = false)
    {
        return new WFWarheadPayload
        {
            DeleteGrid = deleteGrid,
            PromptRadiation = 50f,
            FalloutCount = 2,
            FalloutSpread = 3f,
            FalloutIntensity = 4f,
            FalloutHalfLife = 60f,
            EmpRange = 0f,
            DetonationSound = null,
        };
    }

    [Test]
    public async Task CountdownDetonatesAndLeavesFallout()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var warheads = entMan.System<WFWarheadSystem>();

        var uid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            var design = new WFNukeDesign
            {
                Chassis = Chassis,
                Payload = QuietPayload(),
                Fuzes = WFWarheadFuzes.Timer | WFWarheadFuzes.Remote,
                Timer = 1f,
                Name = "test nuke",
                StartCountdown = true,
            };
            uid = warheads.SpawnDesign(design, map.MapCoords, null);
        });

        await server.WaitAssertion(() =>
        {
            var warhead = entMan.GetComponent<WFWarheadComponent>(uid);
            Assert.That(warhead.State, Is.AnyOf(WFWarheadState.Countdown, WFWarheadState.Final));
            Assert.That(warhead.CustomPayload, Is.Not.Null);
            Assert.That(entMan.HasComponent<WFTimerFuzeComponent>(uid));
            Assert.That(entMan.HasComponent<WFRemoteFuzeComponent>(uid));
            Assert.That(entMan.GetComponent<MetaDataComponent>(uid).EntityName, Is.EqualTo("test nuke"));
            Assert.That(entMan.GetComponent<WFCountdownDisplayComponent>(uid).End, Is.Not.Null);
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(uid), "the warhead is gone after it goes off");

            var fallout = entMan.EntityQuery<WFFalloutComponent, TransformComponent>()
                .Where(f => f.Item2.MapID == map.MapId)
                .ToList();
            Assert.That(fallout, Has.Count.EqualTo(3), "one fallout source at the centre and two scattered");

            var burst = entMan.EntityQuery<RadiationSourceComponent, TransformComponent>()
                .Any(s => s.Item2.MapID == map.MapId && s.Item1.Intensity >= 49f);
            Assert.That(burst, "the radiation burst is still running");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DisarmStopsTheCountdown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var warheads = entMan.System<WFWarheadSystem>();

        var uid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            uid = entMan.SpawnEntity(Keyed, map.GridCoords);
            var comp = entMan.GetComponent<WFWarheadComponent>(uid);
            warheads.StartCountdown((uid, comp), null);
            warheads.Disarm((uid, comp), null);
        });

        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(uid));
            Assert.That(entMan.GetComponent<WFWarheadComponent>(uid).State, Is.EqualTo(WFWarheadState.Safe));
            Assert.That(entMan.GetComponent<WFCountdownDisplayComponent>(uid).End, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TwoKeysFromTwoPeopleArm()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var warheads = entMan.System<WFWarheadSystem>();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(Keyed, map.GridCoords);
            var warhead = entMan.GetComponent<WFWarheadComponent>(uid);
            var keys = entMan.GetComponent<WFTwoPersonArmingComponent>(uid);
            var alice = entMan.SpawnEntity(null, map.GridCoords);
            var bob = entMan.SpawnEntity(null, map.GridCoords);

            warheads.TurnKey((uid, keys), (uid, warhead), alice);
            warheads.TurnKey((uid, keys), (uid, warhead), alice);
            Assert.That(warhead.State, Is.EqualTo(WFWarheadState.Safe), "one person can't turn both keys");

            warheads.TurnKey((uid, keys), (uid, warhead), bob);
            Assert.That(warhead.State, Is.EqualTo(WFWarheadState.Countdown));

            entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeleteGridPayloadRemovesTheGrid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var warheads = entMan.System<WFWarheadSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = mapSys.CreateGridEntity(map.MapId);
            mapSys.SetTile(grid, Vector2i.Zero, map.Tile.Tile);
            entMan.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, new Vector2(50f, 50f));

            var coords = new MapCoordinates(new Vector2(50.5f, 50.5f), map.MapId);
            warheads.Detonate(coords, grid.Owner, QuietPayload(deleteGrid: true), null);

            Assert.That(entMan.Deleted(grid.Owner), "the grid under a delete-grid payload is gone");
            Assert.That(entMan.EntityExists(map.Grid.Owner), "other grids are left alone");
            Assert.That(entMan.EntityExists(map.MapUid), "the map is never deleted");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public void BlastRadiusEstimateGrowsWithYield()
    {
        var none = new WFWarheadPayload { ExplosionType = null, TotalIntensity = 1000f };
        var small = new WFWarheadPayload { ExplosionType = "Default", TotalIntensity = 40000f, IntensitySlope = 4f, MaxIntensity = 60f };
        var big = new WFWarheadPayload { ExplosionType = "Default", TotalIntensity = 1500000f, IntensitySlope = 3f, MaxIntensity = 150f };

        Assert.That(WFWarheadSystem.EstimateBlastRadius(none), Is.Zero);
        Assert.That(WFWarheadSystem.EstimateBlastRadius(small), Is.InRange(5f, 30f));
        Assert.That(WFWarheadSystem.EstimateBlastRadius(big), Is.GreaterThan(WFWarheadSystem.EstimateBlastRadius(small)));
    }
}
