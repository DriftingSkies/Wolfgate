#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.Radar;
using Content.Shared._WG.Performance;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>A radar request walks only the blips near it and is answered no faster than the server allows.</summary>
[TestFixture]
public sealed class WGRadarRequestTest
{
    [Test]
    public async Task BlipsInReachMatchAFullWalk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var radar = entMan.System<RadarBlipSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var other = await pair.CreateTestMap();
        var sources = new List<EntityUid>();
        var spawned = new List<EntityUid>();
        var seen = new List<EntityUid>();
        var unseen = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            sources.Add(entMan.SpawnEntity(null, new MapCoordinates(0, 0, map.MapId)));
            sources.Add(entMan.SpawnEntity(null, new MapCoordinates(-3000, 500, map.MapId)));
            spawned.AddRange(sources);

            seen.Add(Blip(new Vector2(10, 10), 50, map.MapId));
            unseen.Add(Blip(new Vector2(100, 0), 50, map.MapId));
            seen.Add(Blip(new Vector2(5000, 0), 6000, map.MapId));
            unseen.Add(Blip(new Vector2(5000, 0), 100, map.MapId));
            seen.Add(Blip(new Vector2(-2990, 500), 20, map.MapId));
            unseen.Add(Blip(new Vector2(-1500.5f, -1500.5f), 10, map.MapId));
            seen.Add(Blip(new Vector2(1023.9f, -0.1f), 1100, map.MapId));
            seen.Add(Blip(new Vector2(-1024.1f, 2047.9f), 2400, map.MapId));
            unseen.Add(Blip(new Vector2(20000, 20000), 2048, map.MapId));
            unseen.Add(Blip(new Vector2(10, 10), 50, other.MapId));
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var positions = sources.Select(s => xform.GetWorldPosition(s)).ToList();
            var fullWalk = radar.WGBlipsOnMap(map.MapId)
                .Where(b => positions.Any(p => (p - b.Position).LengthSquared() < b.Blip.MaxDistance * b.Blip.MaxDistance))
                .Select(b => b.Uid)
                .ToList();
            var inReach = radar.WGBlipsInReach(map.MapId, sources).Select(b => b.Uid).ToList();

            Assert.That(inReach, Is.EquivalentTo(fullWalk), "Skipping squares must not change which blips a radar gets.");
            Assert.That(inReach, Is.SupersetOf(seen));
            Assert.That(inReach.Intersect(unseen), Is.Empty);

            foreach (var uid in spawned)
            {
                entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();

        EntityUid Blip(Vector2 position, float range, MapId on)
        {
            var uid = entMan.SpawnEntity(null, new MapCoordinates(position, on));
            entMan.AddComponent<PhysicsComponent>(uid);
            var blip = entMan.AddComponent<RadarBlipComponent>(uid);
            blip.Enabled = true;
            blip.MaxDistance = range;
            spawned.Add(uid);
            return uid;
        }
    }

    [Test]
    public async Task RequestsOverTheCapAreNotAnswered()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var radar = server.EntMan.System<RadarBlipSystem>();
        var before = server.CfgMan.GetCVar(WGRadarCVars.MaxRequestHz);

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WGRadarCVars.MaxRequestHz, 0.5f);
            var answered = 0;
            for (var i = 0; i < 5; i++)
            {
                if (radar.WGTakeRequest(pair.Player!))
                    answered++;
            }

            // None, if an earlier test on this pair left an answer still due.
            Assert.That(answered, Is.LessThanOrEqualTo(1), "Back-to-back requests get one answer at most.");
            server.CfgMan.SetCVar(WGRadarCVars.MaxRequestHz, before);
        });

        await pair.CleanReturnAsync();
    }
}
