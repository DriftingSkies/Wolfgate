#nullable enable
#pragma warning disable RA0002 // The test sets the director's book on the outpost by hand.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server.NPC.HTN;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.OutpostRaids;
using Content.Shared.NPC.Components;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Outposts.OutpostFoundingTest;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.OutpostRaids;

/// <summary>The raid director: a raid picked for an outpost with someone home walks in, reaches the claim edge and leaves at dawn.</summary>
[TestFixture]
[TestOf(typeof(WFRaidDirectorSystem))]
public sealed class RaidDirectorTest
{
    [Test]
    public async Task AWildlifePackWalksInAndLeavesAtDawn()
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
        var weather = server.System<WFPlanetWeatherSystem>();
        var director = server.System<WFRaidDirectorSystem>();
        var raids = server.System<WFRaidSystem>();
        var outposts = server.System<WFOutpostSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid world = default, outpost = default;
        var mobs = new List<EntityUid>();

        try
        {
            await server.WaitAssertion(() =>
            {
                world = entMan.GetComponent<CEZMapComponent>(ground).NetworkUid;
                Assert.That(weather.SetMinuteOfDay(world, 1100), Is.True);

                Assert.That(outposts.TryFound(user, SpawnKit(entMan, ground), new EntityCoordinates(ground, Site.X, Site.Y), out var founded, out var reason),
                    Is.True, $"Founding was refused: {reason}.");
                outpost = founded!.Value;
                var comp = entMan.GetComponent<WFOutpostComponent>(outpost);
                var grid = entMan.GetComponent<MapGridComponent>(outpost);
                Assert.That(director.SomeoneHome((outpost, comp, grid)), Is.True, "Precondition: the founder is home.");

                // Wildlife leaves the founder be, so the pack walks its route instead of chasing him.
                entMan.RemoveComponent<NpcFactionMemberComponent>(user);

                var attention = entMan.EnsureComponent<WFRaidAttentionComponent>(outpost);
                attention.Started = true;
                attention.Attention = 30;
                attention.Step = 1;
                Assert.That(director.TryPickRaid((outpost, comp, grid, attention)), Is.True, "The director sent no raid.");
                Assert.That(attention.Attention, Is.EqualTo(5).Within(0.01), "A step 1 raid should spend 25 threat points.");
                Assert.That(raids.Raids, Has.Count.EqualTo(1));
                Assert.That(director.TryPickRaid((outpost, comp, grid, attention)), Is.False, "A second raid was sent in the same window.");
            });

            await server.WaitRunTicks(pair.SecondsToTicks(2));
            await server.WaitAssertion(() =>
            {
                Assert.That(raids.Raids, Is.Empty, "The raid never landed.");
                Assert.That(director.ActiveRaids, Has.Count.EqualTo(1), "The raid found nowhere to walk in.");
                var raid = director.ActiveRaids.Values.First();
                Assert.That(raid.Step, Is.EqualTo(1));
                mobs.AddRange(raid.Mobs);
                Assert.That(mobs, Has.Count.InRange(4, 5), "A step 1 pack is three or four scar rats and a charger.");

                var comp = entMan.GetComponent<WFOutpostComponent>(outpost);
                var (min, max) = outposts.GetClaimZone((outpost, comp, entMan.GetComponent<MapGridComponent>(outpost)));
                var xforms = server.System<SharedTransformSystem>();
                var player = xforms.GetWorldPosition(user);
                foreach (var mob in mobs)
                {
                    var position = xforms.GetWorldPosition(mob);
                    var dx = Math.Max(Math.Max(min.X - position.X, position.X - (max.X + 1)), 0);
                    var dy = Math.Max(Math.Max(min.Y - position.Y, position.Y - (max.Y + 1)), 0);
                    // 20 tiles out along its way in is at least 18 from the claim in a straight line, less scatter.
                    Assert.That(MathF.Sqrt(dx * dx + dy * dy), Is.GreaterThanOrEqualTo(15.5f), "A raid mob walked in too close to the claim.");
                    Assert.That(Vector2.Distance(position, player), Is.GreaterThanOrEqualTo(10), "A raid mob walked in beside a player.");
                    var htn = entMan.GetComponent<HTNComponent>(mob);
                    Assert.That(htn.RootTask.Task, Is.EqualTo(WFRaidDirectorSystem.RaidCompound));
                    Assert.That(htn.SleepPlayerCheckRangeOverride, Is.EqualTo(WFRaidDirectorSystem.SleepRange));
                    Assert.That(entMan.HasComponent<WFTerrainLoaderComponent>(mob), Is.True, "A raid mob doesn't load the ground it walks.");
                }
            });

            var reached = false;
            var trace = string.Empty;
            for (var i = 0; i < 60 && !reached; i++)
            {
                await server.WaitRunTicks(pair.SecondsToTicks(1));
                await server.WaitPost(() =>
                {
                    reached = mobs.Any(mob => entMan.TryGetComponent<WFRaidMobComponent>(mob, out var raider) && raider.ReachedEdge);
                    trace = string.Join("; ", mobs.Select(mob => Describe(entMan, mob)));
                });
            }

            Assert.That(reached, Is.True, $"No raid mob reached the claim edge in a minute: {trace}.");

            await server.WaitPost(() => Assert.That(weather.SetMinuteOfDay(world, 400), Is.True));
            await server.WaitRunTicks(pair.SecondsToTicks(6));
            await server.WaitAssertion(() =>
            {
                Assert.That(mobs.All(mob => entMan.Deleted(mob) || !entMan.HasComponent<WFRaidMobComponent>(mob) || entMan.HasComponent<Content.Shared.Mobs.Components.MobStateComponent>(mob) && !server.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(mob)),
                    Is.True, "A raid mob was still about after dawn.");
                Assert.That(director.ActiveRaids, Is.Empty, "The raid is still on the books after dawn.");
                Assert.That(entMan.GetComponent<WFRaidAttentionComponent>(outpost).Step, Is.EqualTo(1), "A raid that left at dawn was counted as beaten.");
            });
        }
        finally
        {
            await RaidArrivalTest.LeaveAndTeardown(pair, user, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A mob's current operator and steering status, for tracing what it went through.</summary>
    public static string State(IEntityManager entMan, EntityUid mob)
    {
        if (entMan.Deleted(mob))
            return "deleted";
        var task = entMan.TryGetComponent<HTNComponent>(mob, out var htn) && htn.Plan != null ? htn.Plan.CurrentOperator.GetType().Name : "none";
        var steering = entMan.TryGetComponent<Content.Server.NPC.Components.NPCSteeringComponent>(mob, out var steer) ? steer.Status.ToString() : "-";
        return $"{task}/{steering}";
    }

    /// <summary>Where a raid mob is, how far from its edge spot, and what it is doing.</summary>
    public static string Describe(IEntityManager entMan, EntityUid mob)
    {
        if (entMan.Deleted(mob))
            return "deleted";

        var xforms = entMan.System<SharedTransformSystem>();
        var at = xforms.GetWorldPosition(mob);
        var edge = entMan.TryGetComponent<WFRaidMobComponent>(mob, out var raider) ? xforms.ToMapCoordinates(raider.Edge).Position : at;
        var task = !entMan.TryGetComponent<HTNComponent>(mob, out var htn) ? "no HTN"
            : htn.Plan != null ? htn.Plan.CurrentOperator.GetType().Name
            : $"no plan (enabled {htn.Enabled}, planning {htn.PlanningJob != null}, target {htn.Blackboard.ContainsKey(WFRaidDirectorSystem.TargetKey)})";
        var steering = entMan.TryGetComponent<Content.Server.NPC.Components.NPCSteeringComponent>(mob, out var steer) ? steer.Status.ToString() : "no steering";
        return $"{entMan.GetComponent<MetaDataComponent>(mob).EntityPrototype?.ID} at {at} ({Vector2.Distance(at, edge):F1} from edge), {task}, {steering}";
    }
}
