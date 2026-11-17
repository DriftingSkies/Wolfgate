#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.BoardingAction;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.BoardingAction;
using Content.Shared._WG.Encounters;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

/// <summary>Raiders stop a ship before boarding it: an ion disruptor knocks its engines out, then a harpoon hacks it.</summary>
public sealed partial class WGEncounterTest
{
    [TestPrototypes]
    private const string DisablePrototypes = @"
- type: wgEncounter
  id: WGTestDisablerRaider
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: raider
    vessel: Hazel
    side: pirates
    captain: false
    deckhands: 1
    disablers: [ WGIonDisruptor, WGHackHarpoonGunRaider ]
";

    /// <summary>Every hull a raider with disablers flies has a hardpoint for each of them.</summary>
    [Test]
    public async Task RaiderHullsTakeTheirDisablers()
    {
        var protos = Server.ResolveDependency<IPrototypeManager>();
        var hulls = new List<(string Encounter, ProtoId<VesselPrototype> Vessel, List<EntProtoId> Disablers)>();
        foreach (var encounter in protos.EnumeratePrototypes<WGEncounterPrototype>())
        {
            foreach (var ship in encounter.Ships.Where(ship => ship.Disablers.Count > 0))
            {
                var vessels = ship.Tiers.SelectMany(tier => tier.Vessels).Concat(ship.Vessels).ToList();
                if (ship.Vessel is { } single)
                    vessels.Add(single);
                hulls.AddRange(vessels.Select(vessel => (encounter.ID, vessel, ship.Disablers)));
            }
        }

        Assert.That(hulls.Select(hull => hull.Encounter).Distinct().Count(), Is.GreaterThanOrEqualTo(5),
            "The abductors and extorters carry disablers.");

        var grids = new List<(string Encounter, ProtoId<VesselPrototype> Vessel, List<EntProtoId> Disablers, List<EntityUid> Guns)>();
        await Server.WaitAssertion(() =>
        {
            var loader = SEntMan.System<MapLoaderSystem>();
            var x = -52000f;
            foreach (var (encounter, vessel, disablers) in hulls.DistinctBy(hull => hull.Vessel))
            {
                var path = protos.Index(vessel).ShuttlePath;
                Assert.That(loader.TryLoadGrid(MapData.MapId, path, out var loaded, offset: new Vector2(x, -52000f)), Is.True);
                x += 500f;
                var guns = Server.System<WGPirateRetrofitSystem>().Mount(loaded!.Value.Owner, disablers);
                grids.Add((encounter, vessel, disablers, guns));
            }
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            foreach (var (encounter, vessel, disablers, guns) in grids)
            {
                Assert.That(guns.Select(gun => SEntMan.GetComponent<MetaDataComponent>(gun).EntityPrototype!.ID),
                    Is.EquivalentTo(disablers.Select(id => id.Id)), $"{encounter} flies {vessel}, which can't mount all its disablers.");
                foreach (var gun in guns)
                {
                    Assert.That(SEntMan.GetComponent<HardpointAnchorableOnlyComponent>(gun).anchoredTo, Is.Not.Null,
                        $"{SEntMan.ToPrettyString(gun)} on {vessel} sits on a hardpoint.");
                }
            }
        });
    }

    /// <summary>A disruptor hit kills every thruster on the hull for its duration, then they come back.</summary>
    [Test]
    public async Task DisruptorKillsTheEnginesForAWhile()
    {
        EntityUid grid = default;
        var thrusters = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            var path = Server.ResolveDependency<IPrototypeManager>().Index<VesselPrototype>(VictoriaProto).ShuttlePath;
            Assert.That(SEntMan.System<MapLoaderSystem>().TryLoadGrid(MapData.MapId, path, out var loaded,
                offset: new Vector2(-54000, -54000)), Is.True);
            grid = loaded!.Value.Owner;
            var query = SEntMan.EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    thrusters.Add(uid);
            }

            Assert.That(thrusters, Is.Not.Empty);
            var engines = Server.System<WGEngineDisruptorSystem>();
            Assert.That(engines.Disrupt(grid, System.TimeSpan.FromSeconds(2)), Is.True);
            Assert.That(engines.IsDisrupted(grid), Is.True);
            foreach (var thruster in thrusters)
            {
                Assert.That(SEntMan.GetComponent<ApcPowerReceiverComponent>(thruster).PowerDisabled, Is.True,
                    $"{SEntMan.ToPrettyString(thruster)} is knocked out.");
            }
        });

        await RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGEngineDisruptorSystem>().IsDisrupted(grid), Is.False);
            foreach (var thruster in thrusters)
            {
                Assert.That(SEntMan.GetComponent<ApcPowerReceiverComponent>(thruster).PowerDisabled, Is.False,
                    $"{SEntMan.ToPrettyString(thruster)} comes back.");
            }
        });
    }

    /// <summary>
    /// A raider laid on a ship knocks its engines out with the disruptor, then gets a hacking cable into it; cleared, it
    /// lets go of the cable.
    /// </summary>
    [Test]
    public async Task RaiderDisruptsThenHarpoonsItsTarget()
    {
        EntityUid encounter = default;
        WGEncounterShipState ship = default!;
        await Server.WaitAssertion(() =>
        {
            var proto = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>("WGTestDisablerRaider");
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(proto, new MapCoordinates(new Vector2(-50000, -50000), MapData.MapId),
                out encounter, pinned: true), Is.True);
            ship = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["raider"];
        });
        await WaitUntilServer(() => !ship.Building && !ship.Virtual, 600);

        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WGShipDisablerComponent>(ship.Grid).Guns, Has.Count.EqualTo(2));
            var path = Server.ResolveDependency<IPrototypeManager>().Index<VesselPrototype>(VictoriaProto).ShuttlePath;
            var raiderAt = Server.System<SharedTransformSystem>().GetWorldPosition(ship.Grid);
            Assert.That(SEntMan.System<MapLoaderSystem>().TryLoadGrid(MapData.MapId, path, out var loaded,
                offset: raiderAt + new Vector2(120f, 0f)), Is.True);
            target = loaded!.Value.Owner;
            Server.System<WGShipDisablerSystem>().SetTarget(ship.Grid, target);
        });

        var disrupted = false;
        var hacks = Server.System<WGHackHarpoonSystem>();
        for (var i = 0; i < 60 && hacks.CableTarget(ship.Grid) != target; i++)
        {
            await RunSeconds(1);
            await Server.WaitPost(() => disrupted |= Server.System<WGEngineDisruptorSystem>().IsDisrupted(target));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(disrupted, Is.True, "The disruptor knocked the target's engines out.");
            Assert.That(hacks.CableTarget(ship.Grid), Is.EqualTo(target), "The harpoon got a cable into it.");
            Server.System<WGShipDisablerSystem>().SetTarget(ship.Grid, null);
        });
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(hacks.CableTarget(ship.Grid), Is.Null, "Cleared, it lets go of the cable.");
            Server.System<WGEncounterSystem>().End(encounter);
        });
    }
}
