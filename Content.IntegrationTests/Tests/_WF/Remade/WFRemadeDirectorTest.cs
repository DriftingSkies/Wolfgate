#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Shared._WF.Remade;
using Content.Shared.CCVar;
using Content.Shared.DragDrop;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Tiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The AI and the Tap end to end, on a real deck with a real player attached.</summary>
[TestFixture]
public sealed class WFRemadeDirectorTest
{
    private const int DeckRadius = 22;

    /// <summary>Lays a square of plating round the test grid's origin.</summary>
    internal static void LayDeck(TestPair pair, TestMapData map)
    {
        var maps = pair.Server.EntMan.System<SharedMapSystem>();
        var plating = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        for (var x = -DeckRadius; x <= DeckRadius; x++)
        for (var y = -DeckRadius; y <= DeckRadius; y++)
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), plating);
    }

    internal static EntityCoordinates At(TestMapData map, float x, float y)
    {
        return new EntityCoordinates(map.Grid.Owner, new Vector2(x + 0.5f, y + 0.5f));
    }

    /// <summary>
    /// A player's body that lives in the test map's vacuum: a human drops into crit there, even in godmode, and with no
    /// living player near NPCs sleep.
    /// </summary>
    internal static EntityUid SpawnCrewman(TestPair pair, EntityCoordinates coordinates)
    {
        var crewman = pair.Server.EntMan.SpawnEntity("MobCarp", coordinates);
        pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, crewman);
        return crewman;
    }

    /// <summary>What an NPC is doing, for a failure message: where, awake, plan, steering and pathfinding, and the nearest living player.</summary>
    internal static string Describe(TestPair pair, EntityUid uid)
    {
        var entMan = pair.Server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var here = transforms.GetWorldPosition(uid);
        var text = new System.Text.StringBuilder($"at {here}, ");
        text.Append(entMan.HasComponent<ActiveNPCComponent>(uid) ? "awake" : "ASLEEP");

        var nearest = float.MaxValue;
        foreach (var session in pair.Server.PlayerMan.Sessions)
        {
            if (session.AttachedEntity is { } body && !entMan.Deleted(body))
            {
                nearest = Math.Min(nearest, (transforms.GetWorldPosition(body) - here).Length());
                text.Append($" [player {entMan.GetComponent<Content.Shared.Mobs.Components.MobStateComponent>(body).CurrentState}, damage {entMan.GetComponent<Content.Shared.Damage.DamageableComponent>(body).TotalDamage}, on grid {entMan.GetComponent<TransformComponent>(body).GridUid == entMan.GetComponent<TransformComponent>(uid).GridUid}]");
            }
        }

        text.Append($", nearest player {nearest:F1} m");
        if (entMan.TryGetComponent<HTNComponent>(uid, out var htn))
        {
            var hunt = htn.Blackboard.TryGetValue<EntityCoordinates>(WFRemadeDirectorSystem.HuntKey, out var key, entMan)
                ? transforms.ToMapCoordinates(key).Position.ToString()
                : "none";
            text.Append($", hunt key {hunt}, sleep range {htn.SleepPlayerCheckRangeOverride?.ToString() ?? "default"}, htn {(htn.Enabled ? "on" : "off")}, plan {(htn.Plan?.CurrentOperator.GetType().Name ?? "none")}, planning {htn.Planning}");
        }

        if (entMan.TryGetComponent<NPCSteeringComponent>(uid, out var steering))
        {
            text.Append($", steering {steering.Status} failed {steering.FailedPathCount} path {steering.CurrentPath.Count} pathfinding {steering.Pathfind} "
                        + $"to {transforms.ToMapCoordinates(steering.Coordinates).Position} navmesh here {entMan.System<PathfindingSystem>().GetPoly(entMan.GetComponent<TransformComponent>(uid).Coordinates) != null}");
        }
        else
        {
            text.Append(", no steering");
        }

        return text.ToString();
    }

    internal static async Task RunUntil(TestPair pair, Func<bool> done, int seconds)
    {
        var ticks = seconds * 60 / 15;
        for (var i = 0; i < ticks; i++)
        {
            await pair.RunTicksSync(15);
            // The pathfinder answers in real time, so a test can't spin faster than it thinks.
            await Task.Delay(50);
            var finished = false;
            await pair.Server.WaitPost(() => finished = done());
            if (finished)
                return;
        }
    }

    [Test]
    public async Task TheFirstGrowthBuildsUpBeforeAnyWave()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 3, 0));
            var hold = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            hold.FirstQuietMin = 12f;
            hold.FirstQuietMax = 12f;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
            entMan.SpawnEntity("GasVentPump", At(map, 20, 0));
        });

        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await RunUntil(pair, () => director.Seeded, 5);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.FirstWaveAt, Is.GreaterThan(timing.CurTime), "the first Growth had no quiet spell.");
            Assert.That(director.Horde, Is.Empty, "a Remade came before the sighting.");
        });

        await RunUntil(pair, () => director.Horde.Count > 0, 15);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Horde, Has.Count.EqualTo(1), "the sighting was more than one Remade.");
            var behaviour = entMan.GetComponent<WFRemadeBehaviourComponent>(director.Horde.First());
            Assert.That(behaviour.Sighting || behaviour.Mode is WFRemadeMode.Retreat or WFRemadeMode.Hidden, Is.True, "the sighting came to fight.");
            Assert.That(timing.CurTime, Is.LessThan(director.FirstWaveAt), "the sighting came after the quiet spell.");
            Assert.That(director.Phase, Is.EqualTo(WFRemadeDirectorPhase.Quiet), "a wave began during the quiet spell.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheDirectorSendsAWaveOutOfSightAndItHunts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid scion = default;
        EntityUid crewman = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 3, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
            // Too far off for the crewman to see, and it lets the wave come in.
            entMan.SpawnEntity("GasVentPump", At(map, 20, 0));
        });

        await pair.RunSeconds(2f);

        await server.WaitPost(() =>
        {
            var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            Assert.That(director.Biomass, Is.GreaterThanOrEqualTo(director.FirstGrowthBiomass), "biomass wasn't paid out at Growth.");
            director.Phase = WFRemadeDirectorPhase.Stalk;
            director.PhaseEnd = server.ResolveDependency<IGameTiming>().CurTime;
        });

        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await RunUntil(pair, () => director.Horde.Count > 0, 10);

        var startDistance = 0f;
        float Nearest() => director.Horde.Min(remade => (transforms.GetWorldPosition(remade) - transforms.GetWorldPosition(crewman)).Length());
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Horde, Is.Not.Empty, "no wave came.");
            Assert.That(director.Horde.Count, Is.LessThanOrEqualTo(WFRemadeDirectorSystem.HordeCap(WFMonolithStage.Growth)));
            startDistance = Nearest();
            Assert.That(startDistance, Is.GreaterThan(14f), "the wave came in where the crewman could see it.");
        });

        await RunUntil(pair, () => Nearest() < startDistance - 3f, 90);

        await server.WaitAssertion(() =>
        {
            var states = director.Horde.Select(remade =>
                (entMan.TryGetComponent<NPCSteeringComponent>(remade, out var steering) ? steering.Status.ToString() : "no steering")
                + (entMan.HasComponent<ActiveNPCComponent>(remade) ? " awake" : " asleep"));
            Assert.That(Nearest(), Is.LessThan(startDistance - 3f), $"the wave didn't hunt the crewman: {string.Join(", ", states)}.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheDirectorSleepsWithNoCrewNear()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            scion = entMan.SpawnEntity("WFMonolithScion", map.GridCoords);
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 60f;
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            Assert.That(director.Biomass, Is.Zero);
            Assert.That(director.Horde, Is.Empty);
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnInfectorGoesToACorpseAndTurnsIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid corpse = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            corpse = entMan.SpawnEntity("MobHuman", At(map, 4, 0));
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            // NPCs sleep with no living player near, and this one is too far off to draw the Infector.
            SpawnCrewman(pair, At(map, -20, -20));
            entMan.SpawnEntity("WFRemadeInfector", At(map, 0, 0));
        });

        await RunUntil(pair, () => entMan.HasComponent<WFRemadeConvertingComponent>(corpse), 30);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadeConvertingComponent>(corpse), Is.True, "the Infector never reached the corpse.");
        });

        await RunUntil(pair, () => entMan.Deleted(corpse), 20);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(corpse), Is.True, "the corpse never turned.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACorpseOnCorruptionIncubatesAndTurns()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid corpse = default;

        await server.WaitPost(() =>
        {
            var scion = entMan.SpawnEntity("WFMonolithScion", map.GridCoords);
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
            corpse = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
        });

        await RunUntil(pair, () => entMan.HasComponent<WFRemadeIncubatingComponent>(corpse), 15);
        await server.WaitPost(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadeIncubatingComponent>(corpse), Is.True, "a corpse on corruption didn't start incubating.");
            entMan.GetComponent<WFRemadeIncubatingComponent>(corpse).TurnsAt = TimeSpan.Zero;
        });

        await RunUntil(pair, () => entMan.Deleted(corpse), 25);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(corpse), Is.True, "the corpse never turned.");
            Assert.That(entMan.EntityQuery<WFRemadeComponent>().Any(), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FeedingTheTapQuietsTheMonolithAndKeepsTheGear()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default, corpse = default, suit = default;

        await server.WaitPost(() =>
        {
            // Apart, or the bodies push off each other and that breaks the do-after.
            LayDeck(pair, map);
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 0, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 30f;
            var tap = entMan.SpawnEntity("WFMonolithTap", At(map, 2, 0));
            var user = entMan.SpawnEntity("MobHuman", At(map, 2, 2));
            corpse = entMan.SpawnEntity("MobHuman", At(map, 2, 1));
            suit = entMan.SpawnEntity("ClothingOuterBioGeneral", At(map, 4, 4));
            Assert.That(entMan.System<InventorySystem>().TryEquip(corpse, suit, "outerClothing", force: true));
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);

            var drop = new DragDropTargetEvent(user, corpse);
            entMan.EventBus.RaiseLocalEvent(tap, ref drop);
            Assert.That(drop.Handled, Is.True);
        });

        await RunUntil(pair, () => entMan.Deleted(corpse), 10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(corpse), Is.True, "the Tap didn't take the corpse.");
            Assert.That(entMan.Deleted(suit), Is.False, "the corpse's gear went with it.");
            Assert.That(entMan.GetComponent<WFMonolithComponent>(scion).Resonance, Is.LessThan(26f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CorruptionSpreadsAcrossTheDeckAndStopsAtItsLimit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var limit = 0;
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            // A small deck, so a Growth Monolith's share of it is quick to fill.
            CorruptionTest.LayDeck(pair, map, 8);
            limit = WFCorruptionSystem.Limit(WFMonolithStage.Growth, entMan.System<WFCorruptionSystem>().HullTiles(map.Grid.Owner));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 0, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        await RunUntil(pair, () => monolith.Corruption.Count >= limit, 240);
        await pair.RunSeconds(8f);

        await server.WaitAssertion(() =>
        {
            Assert.That(monolith.Corruption.Count, Is.EqualTo(limit));
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASanctuaryBarsDrawAndCorruption()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<Content.Server._WF.Remade.Corruption.WFRemadeSanctuaryComponent>(map.Grid.Owner);
            scion = entMan.SpawnEntity("WFMonolithScion", map.GridCoords);
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
            monolith.Resonance = 40f;
            entMan.System<WFMonolithSystem>().SetDraw((scion, monolith), 1f);
        });
        await pair.RunSeconds(5f);

        await server.WaitAssertion(() =>
        {
            var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
            Assert.That(monolith.Draw, Is.Zero);
            Assert.That(monolith.Corruption, Is.Empty);
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }
}
