#nullable enable
#pragma warning disable RA0002 // The test sets the director's book on the outpost by hand.
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Cargo.Components;
using Content.Server.NPC.HTN;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.OutpostRaids;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.NPC.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Outposts.OutpostFoundingTest;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.OutpostRaids;

/// <summary>Humanoid raiders: a party walks up, its breacher breaks through a prefab wall to the turret behind it, and what they carried is raid loot.</summary>
[TestFixture]
[TestOf(typeof(WFRaidBreachSystem))]
public sealed class RaidBreachTest
{
    /// <summary>The outpost's 3x3 founding square plus one plate east, so the turret at (2, 1) stands inside the walls.</summary>
    private static readonly Vector2i[] Ring =
    {
        new(0, 0), new(0, 1), new(0, 2), new(1, 0), new(1, 2), new(2, 0), new(2, 2), new(3, 1),
    };

    private static readonly Vector2i TurretTile = new(2, 1);

    [Test]
    public async Task ARaiderPartyBreaksInAndItsLootIsNotKept()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() =>
        {
            // With PVS off, wildlife's owner-only alerts log as empty states over a long run.
            server.CfgMan.SetCVar(CVars.NetPVS, true);
            server.CfgMan.SetCVar(PlanetCVars.PlanetWeather, false);
            server.CfgMan.SetCVar(OutpostRaidCVars.Enabled, true);
            server.CfgMan.SetCVar(OutpostRaidCVars.Grace, 0f);
            server.CfgMan.SetCVar(OutpostRaidCVars.Lead, 1f);
            server.CfgMan.SetCVar(OutpostRaidCVars.SpawnMin, 20);
            server.CfgMan.SetCVar(OutpostRaidCVars.SpawnMax, 24);
            server.CfgMan.SetCVar(OutpostRaidCVars.PlayerClearance, 12);
        });
        var (layers, user) = await Setup(pair);
        var ground = layers[0];
        var director = server.System<WFRaidDirectorSystem>();
        var outposts = server.System<WFOutpostSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();
        EntityUid outpost = default, turret = default, breacher = default;
        var walls = new List<EntityUid>();
        var mobs = new List<EntityUid>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var world = entMan.GetComponent<CEZMapComponent>(ground).NetworkUid;
                Assert.That(server.System<Content.Server._WF.Planets.WFPlanetWeatherSystem>().SetMinuteOfDay(world, 1100), Is.True);

                Assert.That(outposts.TryFound(user, SpawnKit(entMan, ground), new EntityCoordinates(ground, Site.X, Site.Y), out var founded, out var reason),
                    Is.True, $"Founding was refused: {reason}.");
                outpost = founded!.Value;
                var east = xforms.ToMapCoordinates(new EntityCoordinates(outpost, 3.5f, 1.5f));
                Assert.That(outposts.TryExtend(user, "Plating", xforms.ToCoordinates(ground, east), out reason), Is.True, $"The plate was refused: {reason}.");

                var grid = entMan.GetComponent<MapGridComponent>(outpost);
                foreach (var tile in Ring)
                    walls.Add(entMan.SpawnEntity("WFPrefabWall", maps.GridTileToLocal(outpost, grid, tile)));
                turret = entMan.SpawnEntity("WFOutpostTurret", maps.GridTileToLocal(outpost, grid, TurretTile));
                Assert.That(entMan.GetComponent<TransformComponent>(turret).Anchored, Is.True, "Precondition: the turret stands anchored.");
                // A turret that fires would settle the raid before the breach; this one is only a target.
                entMan.RemoveComponent<NpcFactionMemberComponent>(turret);
                // The raiders leave the founder be, so they go about the breach instead of chasing him.
                entMan.RemoveComponent<NpcFactionMemberComponent>(user);

                var comp = entMan.GetComponent<WFOutpostComponent>(outpost);
                var attention = entMan.EnsureComponent<WFRaidAttentionComponent>(outpost);
                attention.Started = true;
                attention.Attention = 100;
                attention.Step = 4;

                // A raid comes from somewhere. Landing parties need a raiding camp on the world with someone alive in
                // it, Reaver shuttles a stronghold in reach, and a drifter gang only flies where no Reaver campaign runs.
                var table = director.GetTable(world)!;
                var party = table.Packs.First(pack => pack.Source == WFRaidSource.Camp);
                var reavers = table.Packs.First(pack => pack.Source == WFRaidSource.Reavers);
                var drifters = table.Packs.First(pack => pack.Source == WFRaidSource.Drifters);
                Assert.That(director.SourceStands(party, (outpost, comp)), Is.False, "With no camp on the world, no landing party comes.");
                Assert.That(director.SourceStands(reavers, (outpost, comp)), Is.False, "With no campaign, no Reaver shuttle comes.");
                Assert.That(director.SourceStands(drifters, (outpost, comp)), Is.True, "With no campaign, drifters fly.");
                var camp = RaidArrivalTest.SpawnRaidCamp(server, ground, xforms.GetWorldPosition(outpost) + new System.Numerics.Vector2(90f, 0f));
                Assert.That(director.SourceStands(party, (outpost, comp)), Is.True, "A camp with a guard alive sends landing parties.");
                Assert.That(director.TryGetRaidCamp((outpost, comp), out var from), Is.True);
                Assert.That(from, Is.EqualTo(xforms.GetWorldPosition(camp)));

                Assert.That(director.TryPickRaid((outpost, comp, grid, attention)), Is.True, "The director sent no raid.");
                Assert.That(attention.Attention, Is.EqualTo(40).Within(0.01), "A step 4 raid should spend 60 threat points.");
            });

            await server.WaitRunTicks(pair.SecondsToTicks(2));
            await server.WaitAssertion(() =>
            {
                Assert.That(director.ActiveRaids, Has.Count.EqualTo(1), "The raid found nowhere to walk in.");
                var raid = director.ActiveRaids.Values.First();
                Assert.That(raid.Step, Is.EqualTo(4));
                Assert.That(raid.Props, Has.Count.EqualTo(1), "The party left no quad where it walked in.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(raid.Props[0]).EntityPrototype?.ID, Is.EqualTo("WFRaidRaiderQuad"));
                mobs.AddRange(raid.Mobs);
                Assert.That(mobs, Has.Count.InRange(3, 5), "A landing party is a breacher, one or two reavers and one or two deckhands.");

                var breachers = mobs.Where(m => entMan.GetComponent<WFRaidRaiderComponent>(m).Breacher).ToList();
                Assert.That(breachers, Has.Count.EqualTo(1), "The party has no breacher.");
                breacher = breachers[0];
                foreach (var mob in mobs)
                    Assert.That(entMan.GetComponent<HTNComponent>(mob).RootTask.Task, Is.EqualTo("WFRaidRaiderCompound"), "A raider lost its own HTN.");

                var maul = Carried(entMan, breacher).Single(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WFRaidBreachingMaul");
                Assert.That(entMan.HasComponent<WFRaidLootComponent>(maul), Is.True, "The breacher's maul isn't raid loot.");
                Assert.That(entMan.HasComponent<CargoSellBlacklistComponent>(maul), Is.True, "The breacher's maul can be sold.");
                foreach (var mob in mobs)
                {
                    Assert.That(Carried(entMan, mob).Where(e => !entMan.HasComponent<WFRaidLootComponent>(e)).Select(e => entMan.ToPrettyString(e).ToString()),
                        Is.Empty, "Some raider gear isn't raid loot.");
                }

                // How fast a wall falls is the maul's balance, not this test's.
                var blunt = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Blunt");
                entMan.GetComponent<MeleeWeaponComponent>(maul).Damage = new DamageSpecifier(blunt, FixedPoint2.New(150));
            });

            var turretDown = false;
            var inside = false;
            var trace = string.Empty;
            var seen = new List<string>();
            for (var i = 0; i < 180 && !(turretDown && inside); i++)
            {
                await server.WaitRunTicks(pair.SecondsToTicks(1));
                await server.WaitPost(() =>
                {
                    turretDown = entMan.Deleted(turret);
                    inside |= entMan.GetComponent<TransformComponent>(breacher).GridUid == outpost;
                    var stage = entMan.TryGetComponent<WFRaidBreachComponent>(breacher, out var breach) ? $"{breach.Stage} tile {breach.Tile}" : "no breach";
                    trace = $"{RaidDirectorTest.Describe(entMan, breacher)}, {stage}, edge reached {entMan.GetComponent<WFRaidMobComponent>(breacher).ReachedEdge}";
                    var state = RaidDirectorTest.State(entMan, breacher);
                    if (seen.Count == 0 || seen[^1] != state)
                        seen.Add(state);
                });
            }

            Assert.That(turretDown, Is.True, $"The breacher never destroyed the turret: {trace}. It went through: {string.Join(" > ", seen)}.");
            Assert.That(inside, Is.True, $"The breacher never got into the outpost: {trace}.");

            await server.WaitAssertion(() =>
            {
                Assert.That(walls.Count(w => !entMan.Deleted(w)), Is.LessThan(Ring.Length), "The breacher got in without breaking a wall.");
                Assert.That(entMan.Deleted(entMan.GetComponent<WFOutpostComponent>(outpost).Console), Is.False, "The raiders broke the console.");

                // Loot dropped on the outpost isn't worth anything to it.
                var grid = entMan.GetComponent<MapGridComponent>(outpost);
                var appraisal = outposts.Appraise((outpost, grid));
                var sale = outposts.SaleValue((outpost, grid));
                var maul = Carried(entMan, breacher).Single(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WFRaidBreachingMaul");
                Assert.That(server.System<SharedContainerSystem>().TryRemoveFromContainer(maul, force: true), Is.True);
                xforms.SetCoordinates(maul, new EntityCoordinates(outpost, 1.5f, 0.5f));
                Assert.That(entMan.GetComponent<TransformComponent>(maul).GridUid, Is.EqualTo(outpost));
                Assert.That(outposts.IsKept(maul), Is.False, "An outpost save keeps raid loot.");
                Assert.That(outposts.Appraise((outpost, grid)), Is.EqualTo(appraisal), "Raid loot raised the outpost's appraisal.");
                Assert.That(outposts.SaleValue((outpost, grid)), Is.EqualTo(sale), "Raid loot raised the outpost's sale value.");
            });
        }
        finally
        {
            await RaidArrivalTest.LeaveAndTeardown(pair, user, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Everything a mob carries, contents of containers included.</summary>
    private static List<EntityUid> Carried(IEntityManager entMan, EntityUid mob)
    {
        var found = new List<EntityUid>();
        var pending = new Stack<EntityUid>();
        pending.Push(mob);
        while (pending.TryPop(out var uid))
        {
            if (!entMan.TryGetComponent<ContainerManagerComponent>(uid, out var manager))
                continue;
            foreach (var container in manager.Containers.Values)
            {
                foreach (var item in container.ContainedEntities)
                {
                    found.Add(item);
                    pending.Push(item);
                }
            }
        }

        return found;
    }
}
