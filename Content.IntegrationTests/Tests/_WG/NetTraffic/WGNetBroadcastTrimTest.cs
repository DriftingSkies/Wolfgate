#nullable enable
using System.Numerics;
using Content.Server._Battlefield14.FactionTracking;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.Planets;
using Content.Server.Beam;
using Robust.Shared.Maths;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NetTraffic;

/// <summary>
/// The sector-wide broadcasts go out only when what they carry changed, and beam visuals still reach a player beside
/// the beam.
/// </summary>
[TestFixture]
[TestOf(typeof(FactionTrackingSystem))]
[TestOf(typeof(WGEncounterMarkerSystem))]
[TestOf(typeof(WGWorldFormingSystem))]
[TestOf(typeof(BeamSystem))]
public sealed class WGNetBroadcastTrimTest
{
    private const string Beam = "LightningRevenant";

    [Test]
    public async Task UnchangedBroadcastsAreNotResent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var factions = server.System<FactionTrackingSystem>();
        var markers = server.System<WGEncounterMarkerSystem>();
        var forming = server.System<WGWorldFormingSystem>();

        // Let anything left over from an earlier test settle and go out once.
        await pair.RunTicksSync(pair.SecondsToTicks(3f));
        var factionSends = factions.WgBroadcasts;
        var markerSends = markers.WgBroadcasts;
        var formingSends = forming.WgBroadcasts;

        await pair.RunTicksSync(pair.SecondsToTicks(7f));
        Assert.Multiple(() =>
        {
            // One change is allowed for a leftover that only now finished; the old code sent 7, 3 and 28 times.
            Assert.That(factions.WgBroadcasts - factionSends, Is.LessThanOrEqualTo(1),
                "The faction counts were sent again while they stayed the same.");
            Assert.That(markers.WgBroadcasts - markerSends, Is.LessThanOrEqualTo(1),
                "The encounter markers were sent again while they stayed the same.");
            Assert.That(forming.WgBroadcasts - formingSends, Is.LessThanOrEqualTo(1),
                "The world-forming state was sent again with nothing forming.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BeamVisualsReachANearbyPlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        // The pool runs with PVS off, which sends to everyone; the filter only matters with it on.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var viewer = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(2f, 4f), map.MapId));
            server.PlayerMan.SetAttachedEntity(pair.Player!, viewer);
            var source = entMan.SpawnEntity(null, new MapCoordinates(Vector2.Zero, map.MapId));
            var target = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(5f, 0f), map.MapId));
            server.System<BeamSystem>().TryCreateBeam(source, target, Beam);
        });

        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            var rotated = 0;
            var segments = 0;
            var query = client.EntMan.AllEntityQueryEnumerator<SpriteComponent, MetaDataComponent>();
            while (query.MoveNext(out _, out var sprite, out var meta))
            {
                if (meta.EntityPrototype?.ID != Beam)
                    continue;

                segments++;
                if (!sprite.Rotation.EqualsApprox(Angle.Zero))
                    rotated++;
            }

            Assert.That(segments, Is.GreaterThan(0), "The client was never sent the beam.");
            Assert.That(rotated, Is.EqualTo(segments), "A beam segment beside the player was not turned to the beam.");
        });

        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CVars.NetPVS, false);
        });
        await pair.CleanReturnAsync();
    }
}
