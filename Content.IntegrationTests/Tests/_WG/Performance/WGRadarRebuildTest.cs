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

/// <summary>Between sorts of the blip squares, a radar still gets only live blips at their current positions.</summary>
[TestFixture]
public sealed class WGRadarRebuildTest
{
    [Test]
    public async Task StaleSquaresGiveLiveBlips()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var radar = entMan.System<RadarBlipSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var before = server.CfgMan.GetCVar(WGRadarCVars.RebuildTicks);
        var spawned = new List<EntityUid>();
        var sources = new List<EntityUid>();
        EntityUid deleted = default, moved = default, disabled = default, fled = default, kept = default, late = default;

        await server.WaitPost(() =>
        {
            sources.Add(entMan.SpawnEntity(null, new MapCoordinates(0, 0, map.MapId)));
            spawned.AddRange(sources);
            deleted = Blip(new Vector2(10, 0), 2048);
            moved = Blip(new Vector2(20, 0), 2048);
            disabled = Blip(new Vector2(30, 0), 2048);
            fled = Blip(new Vector2(40, 0), 2048);
            kept = Blip(new Vector2(50, 0), 2048);
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            // Sort on this tick, then hold the squares for the rest of the test.
            server.CfgMan.SetCVar(WGRadarCVars.RebuildTicks, 1);
            var all = radar.WGBlipsInReach(map.MapId, sources).Select(b => b.Uid).ToList();
            server.CfgMan.SetCVar(WGRadarCVars.RebuildTicks, 30);
            Assert.That(all, Is.SupersetOf(new[] { deleted, moved, disabled, fled, kept }));

            entMan.DeleteEntity(deleted);
            xform.SetWorldPosition(moved, new Vector2(1500, 0));
            entMan.GetComponent<RadarBlipComponent>(disabled).Enabled = false;
            xform.SetWorldPosition(fled, new Vector2(20000, 0));
            late = Blip(new Vector2(5000, 0), 6000);
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var inReach = radar.WGBlipsInReach(map.MapId, sources);
            var uids = inReach.Select(b => b.Uid).ToList();
            Assert.That(uids, Does.Not.Contain(deleted), "A deleted blip is left out.");
            Assert.That(uids, Does.Not.Contain(disabled), "A switched-off blip is left out.");
            Assert.That(uids, Does.Not.Contain(fled), "A blip that moved out of reach is left out.");
            Assert.That(uids, Does.Contain(kept));
            Assert.That(uids, Does.Contain(late), "A blip added between sorts is found, with the range set after adding it.");
            Assert.That(inReach.Single(b => b.Uid == moved).Position.X, Is.EqualTo(1500f).Within(0.01f),
                "A moved blip is given where it is now.");
            var onMap = radar.WGBlipsOnMap(map.MapId).Select(b => b.Uid).ToList();
            Assert.That(onMap, Does.Not.Contain(deleted));
            Assert.That(onMap, Does.Not.Contain(disabled));
        });

        await server.WaitAssertion(() =>
        {
            // Sorting every tick must give what a walk of every blip gives.
            server.CfgMan.SetCVar(WGRadarCVars.RebuildTicks, 1);
            var sourcePositions = sources.Select(s => xform.GetWorldPosition(s)).ToList();
            var fullWalk = new List<EntityUid>();
            var query = entMan.EntityQueryEnumerator<RadarBlipComponent, TransformComponent, PhysicsComponent>();
            while (query.MoveNext(out var uid, out var blip, out var blipXform, out _))
            {
                var position = xform.GetWorldPosition(blipXform);
                if (blip.Enabled && blipXform.MapID == map.MapId
                    && sourcePositions.Any(p => (p - position).LengthSquared() < blip.MaxDistance * blip.MaxDistance))
                    fullWalk.Add(uid);
            }

            var inReach = radar.WGBlipsInReach(map.MapId, sources).Select(b => b.Uid).ToList();
            Assert.That(inReach, Is.EquivalentTo(fullWalk));
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WGRadarCVars.RebuildTicks, before);
            foreach (var uid in spawned)
            {
                if (entMan.EntityExists(uid))
                    entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();

        EntityUid Blip(Vector2 position, float range)
        {
            var uid = entMan.SpawnEntity(null, new MapCoordinates(position, map.MapId));
            entMan.AddComponent<PhysicsComponent>(uid);
            var blip = entMan.AddComponent<RadarBlipComponent>(uid);
            blip.Enabled = true;
            blip.MaxDistance = range;
            spawned.Add(uid);
            return uid;
        }
    }
}
