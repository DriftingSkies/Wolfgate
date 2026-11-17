#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcDialogue;
using Content.Server._WG.NpcDialogue.Components;
using Content.Server._WG.NpcDialogue.Systems;
using Content.Shared._Mono.CCVar;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared.CombatMode.Pacification;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.NpcDialogue;

/// <summary>
/// Encounter ships talk by how they are doing: a side warns a vessel once and then once more, a badly hurt ship makes no
/// demands and picks no fights, a beaten one may surrender and stand its crew down, and firing on it after ends that.
/// </summary>
[TestOf(typeof(WGNpcDialogueSystem))]
public sealed class WGNpcDialogueTest : InteractionTest
{
    private const string Raider = "WGTestDialogueRaider";
    private const string Pirate = "wg-encounter-pirate-zone";

    [TestPrototypes]
    private const string Prototypes = @"
- type: wgEncounter
  id: WGTestDialogueRaider
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: raider
    vessel: WGDredger
    distress: false
    evades: false
    warnRange: 1200
    attackRange: 300
    zoneLines: wg-encounter-pirate-zone
    objectives:
    - kind: Hold
";

    [SetUp]
    public async Task DisableRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, false));
    }

    [TearDown]
    public async Task RestoreRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, true));
    }

    /// <summary>A raider and the player's bare ship <paramref name="distance"/> metres off it, the player aboard.</summary>
    private async Task<(EntityUid Encounter, EntityUid Intruder)> Spawn(Vector2 at, float distance)
    {
        EntityUid encounter = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var proto = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(Raider);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(proto, new MapCoordinates(at, MapData.MapId), out encounter), Is.True);
        });
        await WaitUntil(() => Ship(encounter) is { Building: false } ship
                              && Server.System<WGNpcDialogueSystem>().CrewAboard(ship) > 0, 600);
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var grid = maps.CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            maps.SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, at + new Vector2(distance, 0f)));
            transform.SetCoordinates(SPlayer, new EntityCoordinates(intruder, new Vector2(0.5f)));
        });
        return (encounter, intruder);
    }

    private WGEncounterShipState Ship(EntityUid encounter) => SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["raider"];

    private async Task End(EntityUid encounter, EntityUid intruder)
    {
        await Server.WaitPost(() =>
        {
            Server.System<WGEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }

    /// <summary>Clears every cooldown between two warnings, so the next look may speak at once.</summary>
    private void ClearCooldowns(EntityUid encounter, EntityUid intruder)
    {
        var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
        var ship = comp.Ships["raider"];
        comp.Hailed.Clear();
        ship.Warned.Remove(intruder);
        if (comp.Talk.TryGetValue((ship.Side, intruder), out var talk))
            talk.NextWarn = TimeSpan.Zero;
        Server.System<WGNpcRadioSystem>().Reset();
    }

    private async Task WaitUntil(Func<bool> condition, int maxTicks)
    {
        var met = false;
        for (var waited = 0; waited < maxTicks && !met; waited += 20)
        {
            await RunTicks(20);
            await Server.WaitPost(() => met = condition());
        }

        Assert.That(met, Is.True, "The condition was not met in time.");
    }

    /// <summary>A side warns a vessel in its zone once, then gives a final warning, then says nothing more.</summary>
    [Test]
    public async Task WarnsOnceThenFinallyThenFallsSilent()
    {
        var (encounter, intruder) = await Spawn(new Vector2(31000, 31000), 800f);
        await Server.WaitPost(() => Server.System<WGNpcRadioSystem>().Reset());
        WGTalkState? talk = null;
        await WaitUntil(() => SEntMan.GetComponent<WGEncounterComponent>(encounter).Talk
            .TryGetValue((Ship(encounter).Side, intruder), out talk) && talk.Stage == 1, 300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(talk!.Last, Does.StartWith($"{Pirate}-warn-"), "A whole raider makes its usual demand.");
            Assert.That(Ship(encounter).Warned.ContainsKey(intruder), Is.True);
            ClearCooldowns(encounter, intruder);
        });

        await WaitUntil(() => talk!.Stage == 2, 300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(talk!.Last, Does.StartWith($"{Pirate}-final-"), "The second warning is the last.");
            ClearCooldowns(encounter, intruder);
        });

        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(talk!.Stage, Is.EqualTo(2), "After the final warning the side holds its peace.");
            Assert.That(talk.Last, Does.StartWith($"{Pirate}-final-"));
        });
        await End(encounter, intruder);
    }

    /// <summary>A badly hurt raider pleads instead of demanding, and does not open fire on a vessel inside its attack zone.</summary>
    [Test]
    public async Task DesperateShipMakesNoDemandsAndPicksNoFight()
    {
        var (encounter, intruder) = await Spawn(new Vector2(33000, 31000), 200f);
        await Server.WaitPost(() =>
        {
            var ship = Ship(encounter);
            // In a fight its mood only gets worse, so the hurt it is given stays.
            ship.LastFought = Server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromMinutes(10);
            ship.Mood = WGShipMood.Desperate;
            ship.SpokenMood = WGShipMood.Desperate;
            Server.System<WGNpcRadioSystem>().Reset();
        });

        WGTalkState? talk = null;
        await WaitUntil(() => SEntMan.GetComponent<WGEncounterComponent>(encounter).Talk
            .TryGetValue((Ship(encounter).Side, intruder), out talk) && talk.Stage >= 1, 300);
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            var ship = Ship(encounter);
            Assert.That(ship.Mood, Is.EqualTo(WGShipMood.Desperate));
            Assert.That(talk!.Last, Does.StartWith($"{Pirate}-warn-desperate-"), "A desperate raider asks to be left alone.");
            Assert.That(ship.Engaged, Does.Not.Contain(intruder), "It picks no fight inside its attack zone.");
        });
        await End(encounter, intruder);
    }

    /// <summary>
    /// A surrendered ship stands its crew down, holds still and is out of the fight; a player ship firing on it once the
    /// grace has passed makes it fight to the end, crew restored.
    /// </summary>
    [Test]
    public async Task SurrenderStandsCrewDownAndBetrayalEndsIt()
    {
        var (encounter, intruder) = await Spawn(new Vector2(35000, 31000), 800f);
        await Server.WaitAssertion(() =>
        {
            var ship = Ship(encounter);
            var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
            Assert.That(Server.System<WGShipSurrenderSystem>().Surrender((encounter, comp), ship, intruder), Is.True);
            Assert.That(ship.Surrendered, Is.True);
            Assert.That(SEntMan.HasComponent<WGCrewSurrenderedComponent>(ship.Grid), Is.True);
            Assert.That(Server.System<WGEncounterSystem>().InFight(ship), Is.False, "A surrendered ship is out of the fight.");

            var crew = Crew(ship);
            Assert.That(crew, Is.Not.Empty);
            foreach (var (uid, member) in crew)
            {
                Assert.That(member.Engagement, Is.EqualTo(WGCrewEngagement.Never), $"{SEntMan.ToPrettyString(uid)} stands down.");
                Assert.That(member.Duty, Is.Not.EqualTo(WGCrewDuties.Gunnery), "Nobody stays on the guns.");
                Assert.That(SEntMan.HasComponent<PacifiedComponent>(uid), Is.True);
            }
        });

        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            var ship = Ship(encounter);
            Assert.That(ship.Surrendered, Is.True, "Nothing has fired on it, so it stays surrendered.");
            // Past the grace for shots already in flight, a hit is a betrayal.
            ship.SurrenderedAt -= WGShipSurrenderSystem.Grace * 2;
            var hit = new WGCrewHullHitEvent(ship.Grid, intruder);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref hit);

            Assert.That(ship.Surrendered, Is.False, "Fired on, it takes back its surrender.");
            Assert.That(ship.LastStand, Is.True, "It never offers again.");
            Assert.That(SEntMan.HasComponent<WGCrewSurrenderedComponent>(ship.Grid), Is.False);
            foreach (var (uid, member) in Crew(ship))
            {
                Assert.That(member.Engagement, Is.Not.EqualTo(WGCrewEngagement.Never), $"{SEntMan.ToPrettyString(uid)} fights again.");
                Assert.That(SEntMan.HasComponent<PacifiedComponent>(uid), Is.False);
                Assert.That(SEntMan.HasComponent<WGSurrenderedCrewComponent>(uid), Is.False);
            }
        });
        await End(encounter, intruder);
    }

    /// <summary>A ship beaten in a fight with a player vessel speaks of it once and rolls its surrender only once.</summary>
    [Test]
    public async Task BeatenShipRollsItsSurrenderOnce()
    {
        var (encounter, intruder) = await Spawn(new Vector2(37000, 31000), 800f);
        await Server.WaitPost(() =>
        {
            var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
            var ship = Ship(encounter);
            var now = Server.ResolveDependency<IGameTiming>().CurTime;
            comp.Fire[(intruder, ship.Side)] = Enumerable.Repeat(now + TimeSpan.FromMinutes(1), 5).ToList();
            ship.LastFought = now + TimeSpan.FromMinutes(10);
            ship.Mood = WGShipMood.Broken;
            Server.System<WGNpcRadioSystem>().Reset();
        });

        await WaitUntil(() => Ship(encounter).SurrenderRolled, 300);
        await Server.WaitAssertion(() =>
        {
            var ship = Ship(encounter);
            Assert.That(ship.SpokenMood, Is.EqualTo(WGShipMood.Broken));
            if (ship.Surrendered)
                Assert.That(ship.SurrenderedTo, Is.EqualTo(intruder));
        });
        await End(encounter, intruder);
    }

    /// <summary>A second crew talking over the first on a channel is dropped, a mayday never is, and routine traffic is capped.</summary>
    [Test]
    public async Task RadioBudgetDropsChorusAndCapsRoutine()
    {
        await Server.WaitAssertion(() =>
        {
            var radio = Server.System<WGNpcRadioSystem>();
            radio.Reset();
            // Two crews off any grid, each its own vessel.
            var a = SEntMan.SpawnEntity(null, new MapCoordinates(new Vector2(39000, 31000), MapData.MapId));
            var b = SEntMan.SpawnEntity(null, new MapCoordinates(new Vector2(39100, 31000), MapData.MapId));

            Assert.That(radio.Send(a, "first", WGNpcDialogueSystem.Channel), Is.True);
            Assert.That(radio.Send(b, "over it", WGNpcDialogueSystem.Channel), Is.False, "Another crew talking over the first is dropped.");
            Assert.That(radio.Send(b, "mayday", WGNpcDialogueSystem.Channel, WGNpcRadioPriority.High), Is.True, "A mayday always goes out.");

            radio.Reset();
            var sent = 0;
            for (var i = 0; i < 20; i++)
            {
                if (radio.Send(a, "routine", WGNpcDialogueSystem.Channel, WGNpcRadioPriority.Routine))
                    sent++;
            }

            Assert.That(sent, Is.LessThan(20), "Routine traffic is capped.");
            Assert.That(sent, Is.GreaterThan(0));
            radio.Reset();
            SEntMan.DeleteEntity(a);
            SEntMan.DeleteEntity(b);
        });
    }

    private System.Collections.Generic.List<(EntityUid Uid, WGCrewComponent Crew)> Crew(WGEncounterShipState ship)
    {
        var found = new System.Collections.Generic.List<(EntityUid, WGCrewComponent)>();
        var query = SEntMan.EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var member, out var xform))
        {
            if (member.Group == ship.Group && xform.GridUid == ship.Grid
                && Server.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(uid))
                found.Add((uid, member));
        }

        return found;
    }
}
