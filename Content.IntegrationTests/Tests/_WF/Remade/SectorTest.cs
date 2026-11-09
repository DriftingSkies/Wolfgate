#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Encounters;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Garrison;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Structures;
using Content.Server._WF.Remade.Sector;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Sector;
using Content.Shared._WF.Remade.Structures;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Localization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The Remade in the open sector: sites, the discovery rule, the signal, the Choir, infected crews and jumps.</summary>
[TestFixture]
public sealed class SectorTest
{
    private const string Hulk = "WFRemadeSiteHulk";
    private const string Guarded = "WFRemadeSiteGuarded";
    private const string Adrift = "WFRemadeSiteAdrift";

    /// <summary>Where a site's encounter is put on the test map, well clear of its grid and a long way from the player.</summary>
    private static MapCoordinates Far(TestMapData map, float x = 3000f)
    {
        return new MapCoordinates(new Vector2(x, 0f), map.MapId);
    }

    private static List<WFEncounterMarker> Collect(IEntityManager entMan)
    {
        var markers = new List<WFEncounterMarker>();
        var ev = new WFEncounterCollectMarkersEvent(markers);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return markers;
    }

    private static (EntityUid Encounter, EntityUid Scion, EntityUid Grid) SiteOf(IEntityManager entMan, string key)
    {
        var encounter = entMan.AllEntities<WFRemadeSiteComponent>().Select(site => site.Owner).Single();
        var comp = entMan.GetComponent<WFRemadeSiteComponent>(encounter);
        var ships = entMan.GetComponent<WFEncounterComponent>(encounter).Ships;
        return (encounter, comp.Scion ?? default, ships[key].Grid);
    }

    [Test]
    public async Task EveryLineASiteSpeaksExists()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var keys = new List<string> { "wf-remade-sector-hint-sender", "wf-remade-choir-zone-attack", "wf-remade-signal-adrift" };
            for (var i = 1; i <= 3; i++)
            {
                keys.Add($"wf-remade-sector-hint-{i}");
                keys.Add($"wf-remade-choir-zone-warn-{i}");
            }

            foreach (var stage in Enum.GetValues<WFMonolithStage>())
                keys.Add($"wf-remade-signal-stage-{stage}");

            foreach (var site in proto.EnumeratePrototypes<WFRemadeSitePrototype>())
            {
                var encounter = proto.Index(site.Encounter);
                keys.Add(encounter.Name);
                if (encounter.Announcement is { } announcement)
                    keys.Add(announcement);
            }

            foreach (var key in keys)
            {
                Assert.That(loc.TryGetString(key, out _), Is.True, $"{key} has no text.");
            }

            Assert.That(loc.TryGetString("ent-WFMobChoirist", out var name), Is.True);
            Assert.That(name, Is.EqualTo("chorister"));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheRulePlacesOneDormantScionAndOnlyOneAndItCleansUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var ticker = entMan.System<GameTicker>();
        var sites = entMan.System<WFRemadeSiteSystem>();
        var encounters = entMan.System<WFEncounterSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var config = server.ResolveDependency<IConfigurationManager>();
        EntityUid rule = default;

        await server.WaitPost(() =>
        {
            Assert.That(sites.Enabled, Is.False, "the sector rules are on without being asked to be.");
            config.SetCVar(EncountersCVars.Enabled, true);
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            ticker.StartGameRule("WFRemadeDiscoveryRule", out rule);
            var comp = entMan.GetComponent<WFRemadeDiscoveryRuleComponent>(rule);
            comp.Next = TimeSpan.Zero;
            comp.Chance = 1f;
            comp.CheckMin = comp.CheckMax = 1f;
            comp.MinGap = 0f;
            comp.GoneCooldown = 0f;
        });

        // Chance is certain and it rolls every second, so only the one-at-a-time rule can hold the count at one.
        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.AllEntities<WFRemadeSiteComponent>().Any(site => site.Comp.Populated), 90);
        await pair.RunSeconds(15f);

        EntityUid encounter = default, scion = default, grid = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<WFRemadeSiteComponent>().Count(), Is.EqualTo(1), "the rule placed more than one site.");
            Assert.That(entMan.EntityQuery<WFMonolithComponent>().Count(), Is.EqualTo(1), "there is not exactly one Scion in the sector.");
            (encounter, scion, grid) = SiteOf(entMan, "hulk");
            var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
            var signal = entMan.GetComponent<WFScionSignalComponent>(scion);
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Quiet), "the discovered Scion isn't dormant.");
            Assert.That(entMan.GetComponent<TransformComponent>(scion).GridUid, Is.EqualTo(grid), "the Scion isn't on the hulk.");
            Assert.That(entMan.GetComponent<TransformComponent>(scion).Anchored, Is.True, "the Scion isn't wrenched down.");
            Assert.That(signal.Wild && !signal.Disturbed, Is.True);

            var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            Assert.That(director.Biomass, Is.Zero, "the Director paid out with nobody near.");
            Assert.That(director.Horde, Is.Empty);
            Assert.That(entMan.EntityQuery<WFRemadeComponent>(), Is.Empty);

            var comp = entMan.GetComponent<WFEncounterComponent>(encounter);
            Assert.That(comp.Hidden, Is.False, "the site has no marker on the sector map.");
            Assert.That(Collect(entMan), Is.Empty, "a dormant Scion shows on the radar.");
            Assert.That(sites.WildCount(), Is.EqualTo(1));
            Assert.That(sites.CanStartWild(0f, 0f, out _), Is.False, "a second wild Scion is allowed.");
        });

        // It ends: a Scion that goes is not replaced at once, and an encounter that runs out takes its hull and Scion away.
        await server.WaitPost(() =>
        {
            // The rule would place the next at once, with its limits off.
            ticker.EndGameRule(rule);
            entMan.GetComponent<WFEncounterComponent>(encounter).Expires = timing.CurTime;
        });
        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(grid), 120);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(grid), Is.True, "the hulk wasn't cleaned up.");
            Assert.That(entMan.Deleted(scion), Is.True, "the Scion outlived its hulk.");
            Assert.That(sites.WildCount(), Is.Zero);
            Assert.That(sites.CanStartWild(0f, 1800f, out _), Is.False, "another Scion followed the lost one at once.");
            Assert.That(sites.CanStartWild(0f, 0f, out _), Is.True);
            config.SetCVar(EncountersCVars.Enabled, EncountersCVars.Enabled.DefaultValue);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            if (!entMan.Deleted(encounter))
                encounters.End(encounter);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASimulatedHulkGetsItsScionWhenCrewComeNear()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sites = entMan.System<WFRemadeSiteSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        EntityUid crewman = default, encounter = default;

        await server.WaitPost(() =>
        {
            config.SetCVar(EncountersCVars.Simulate, true);
            config.SetCVar(EncountersCVars.MaterializeInterval, 0.1f);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            var site = server.ResolveDependency<IPrototypeManager>().Index<WFRemadeSitePrototype>(Hulk);
            Assert.That(sites.TryStartSite(site, Far(map, 9000f), false, out encounter), Is.True);
        });
        await pair.RunSeconds(8f);

        // Far off the hull is a point on the map: no Scion exists yet, but one is still counted so no second is placed.
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFRemadeSiteComponent>(encounter).Populated, Is.False);
            Assert.That(entMan.EntityQuery<WFMonolithComponent>(), Is.Empty);
            Assert.That(sites.WildCount(), Is.EqualTo(1), "an unbuilt hulk isn't counted as a wild Scion.");
            Assert.That(sites.CanStartWild(0f, 0f, out _), Is.False);
            transforms.SetCoordinates(crewman, new EntityCoordinates(map.MapUid, new Vector2(7500f, 0f)));
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.GetComponent<WFRemadeSiteComponent>(encounter).Populated, 90);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFRemadeSiteComponent>(encounter).Populated, Is.True, "the hulk was built and got no Scion.");
            var (_, scion, grid) = SiteOf(entMan, "hulk");
            Assert.That(entMan.GetComponent<TransformComponent>(scion).GridUid, Is.EqualTo(grid));
            Assert.That(entMan.GetComponent<WFEncounterComponent>(encounter).Ships["hulk"].Touched, Is.True, "the hull could go back to simulation and lose its Scion.");
            Assert.That(sites.WildCount(), Is.EqualTo(1));
            entMan.System<WFEncounterSystem>().End(encounter);
            config.SetCVar(EncountersCVars.Simulate, false);
            config.SetCVar(EncountersCVars.MaterializeInterval, EncountersCVars.MaterializeInterval.DefaultValue);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADormantScionIsSilentUntilDisturbedThenShowsAndStrengthens()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid scion = default, drawn = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = entMan.SpawnEntity("WFMonolithScion", WFRemadeDirectorTest.At(map, 6, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            var signal = entMan.EnsureComponent<WFScionSignalComponent>(scion);
            signal.Wild = true;
            signal.Home = map.Grid.Owner;
            transforms.AnchorEntity(scion);

            // A Scion nobody moved but somebody draws on hard shows all the same.
            drawn = entMan.SpawnEntity("WFMonolithScion", WFRemadeDirectorTest.At(map, -6, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(drawn).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(drawn).Draw = 0.5f;
        });
        await pair.RunSeconds(3f);

        float Range() => Collect(entMan).Single(marker => marker.Encounter == entMan.GetNetEntity(scion)).WarnRange;
        WFEncounterMarker Marker() => Collect(entMan).Single(marker => marker.Encounter == entMan.GetNetEntity(scion));

        await server.WaitAssertion(() =>
        {
            var markers = Collect(entMan);
            Assert.That(markers.Any(marker => marker.Encounter == entMan.GetNetEntity(scion)), Is.False, "a Scion at rest is on the radar.");
            Assert.That(markers.Any(marker => marker.Encounter == entMan.GetNetEntity(drawn)), Is.True, "a Scion drawn on hard is not.");
            transforms.Unanchor(scion);
        });
        await pair.RunSeconds(3f);

        float quiet = 0f, hymn = 0f, rapture = 0f;
        await server.WaitAssertion(() =>
        {
            var marker = Marker();
            quiet = marker.WarnRange;
            Assert.That(marker.Map, Is.EqualTo(map.MapId));
            Assert.That(marker.Category, Is.EqualTo(WFEncounterCategory.Threat));
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 20f;
        });
        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            hymn = Range();
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 80f;
        });
        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            rapture = Range();
            Assert.That(Marker().Threat, Is.GreaterThan(1), "the rank didn't climb with the stage.");
            Assert.That(quiet, Is.LessThan(hymn), "the signal didn't widen with resonance.");
            Assert.That(hymn, Is.LessThan(rapture), "the signal didn't widen with resonance.");
        });

        // The markers go out to everyone, not just to code that asks.
        await pair.RunSeconds(4f);
        var received = pair.Client.EntMan.System<WFEncounterMarkerClientSystem>().Markers;
        NetEntity net = default;
        await server.WaitPost(() => net = entMan.GetNetEntity(scion));
        Assert.That(received.Any(marker => marker.Encounter == net && marker.WarnRange > hymn), Is.True,
            "the client never got the Scion's signal.");

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(scion);
            entMan.DeleteEntity(drawn);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheCommandPlacesASiteAndSetsResonance()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var console = server.ResolveDependency<IConsoleHost>();

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            console.ExecuteCommand($"wf_remade spawn {Hulk}");
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.AllEntities<WFRemadeSiteComponent>().Any(site => site.Comp.Populated), 90);

        EntityUid scion = default;
        await server.WaitPost(() =>
        {
            Assert.That(entMan.EntityQuery<WFRemadeSiteComponent>().Count(), Is.EqualTo(1), "wf_remade spawn didn't place the site.");
            scion = SiteOf(entMan, "hulk").Scion;
            Assert.That(entMan.GetComponent<WFMonolithComponent>(scion).Stage, Is.EqualTo(WFMonolithStage.Quiet));
            console.ExecuteCommand("wf_remade resonance 60");
            console.ExecuteCommand("wf_remade status");
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
            Assert.That(monolith.Resonance, Is.GreaterThanOrEqualTo(60f), "wf_remade resonance didn't set the wild Scion.");
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Harvest));
            Assert.That(entMan.GetComponent<WFScionSignalComponent>(scion).Disturbed, Is.True, "a Scion at Harvest is still silent.");
            var encounter = SiteOf(entMan, "hulk").Encounter;
            entMan.System<WFEncounterSystem>().End(encounter);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnAdriftHullSleepsUntilCrewComeThenItsCrewTurnAndTheEncounterStaysTidy()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sites = entMan.System<WFRemadeSiteSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        var mobs = entMan.System<MobStateSystem>();
        var inventory = entMan.System<InventorySystem>();
        EntityUid crewman = default, encounter = default, scion = default, grid = default;

        await server.WaitPost(() =>
        {
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            // The Director only wakes for a living crewman, and the carp sometimes died under CI load before it got there.
            entMan.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(crewman);
            var site = server.ResolveDependency<IPrototypeManager>().Index<WFRemadeSitePrototype>(Adrift);
            Assert.That(sites.TryStartSite(site, Far(map), false, out encounter), Is.True, "the adrift site didn't start.");
        });
        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.GetComponent<WFRemadeSiteComponent>(encounter).Populated, 60);
        await pair.RunSeconds(3f);

        WFMonolithComponent monolith = default!;
        WFRemadeDirectorComponent director = default!;
        await server.WaitAssertion(() =>
        {
            (_, scion, grid) = SiteOf(entMan, "adrift");
            monolith = entMan.GetComponent<WFMonolithComponent>(scion);
            director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Harvest), "the adrift hull isn't deep in an outbreak.");
            Assert.That(entMan.EntityQuery<WFCrewComponent>().Count(), Is.GreaterThan(0), "nobody is left aboard to call for help.");
        });

        // The hull is its own world: corruption spreads over it while nobody looks, and the Director sleeps.
        await WFRemadeDirectorTest.RunUntil(pair, () => monolith.Corruption.Count >= 3, 60);
        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(monolith.Corruption.Count, Is.GreaterThanOrEqualTo(3), "no corruption on the infected hull.");
            Assert.That(director.Biomass, Is.Zero, "the Director paid out with nobody near.");
            Assert.That(director.Seeded, Is.False);
            Assert.That(director.Horde, Is.Empty);
            Assert.That(Collect(entMan).Count(marker => marker.Grid == entMan.GetNetEntity(grid)), Is.GreaterThanOrEqualTo(0));
        });

        // One of its hands dies on the corruption and rises like anyone else, leaving the gear behind.
        EntityUid victim = default;
        List<EntityUid> gear = new();
        await server.WaitPost(() =>
        {
            victim = entMan.AllEntities<WFCrewComponent>().Select(crew => crew.Owner).First();
            gear = inventory.GetHandOrInventoryEntities(victim).ToList();
            Assert.That(gear, Is.Not.Empty, "the crewman carries nothing.");
            var tile = monolith.Corruption.First();
            transforms.SetCoordinates(victim, entMan.GetComponent<TransformComponent>(tile).Coordinates);
            mobs.ChangeMobState(victim, MobState.Dead);
        });
        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.HasComponent<WFRemadeIncubatingComponent>(victim), 30);
        await server.WaitPost(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadeIncubatingComponent>(victim), Is.True, "a crewman dead on corruption never started to turn.");
            entMan.GetComponent<WFRemadeIncubatingComponent>(victim).TurnsAt = TimeSpan.Zero;
        });
        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(victim), 45);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(victim), Is.True, "the crewman never turned.");
            Assert.That(entMan.EntityQuery<WFRemadeComponent>().Any(remade => remade.Source == scion), Is.True, "nothing rose, or it answers to no Monolith.");
            Assert.That(gear.All(item => !entMan.Deleted(item)), Is.True, "the crewman's gear went with him.");
        });

        // With the rest of them dead the ship has no crew and the encounter closes: nothing hangs on a hull that can't be flown.
        await server.WaitPost(() =>
        {
            foreach (var crew in entMan.AllEntities<WFCrewComponent>().Select(c => c.Owner).ToList())
            {
                mobs.ChangeMobState(crew, MobState.Dead);
            }
        });
        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.GetComponent<WFEncounterComponent>(encounter).Resolution != null, 40);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed),
                "an encounter with every hand dead didn't close.");
        });

        // Crew near the Monolith wake the Director.
        await server.WaitPost(() => transforms.SetCoordinates(crewman, entMan.GetComponent<TransformComponent>(scion).Coordinates));
        await WFRemadeDirectorTest.RunUntil(pair, () => director.Seeded, 15);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Seeded, Is.True, $"the Director never woke for the crew: crewman {entMan.GetComponent<Content.Shared.Mobs.Components.MobStateComponent>(crewman).CurrentState}, "
                + $"{(transforms.GetWorldPosition(crewman) - transforms.GetWorldPosition(scion)).Length():F1} m from a {monolith.Stage} stone.");
            Assert.That(director.Biomass, Is.GreaterThan(0f));
            entMan.System<WFEncounterSystem>().End(encounter);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheDirectorAndCorruptionKeepWorkingAfterAJump()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var shuttles = entMan.System<ShuttleSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var growths = entMan.System<WFGrowthSystem>();
        var shuttle = source.Grid.Owner;
        EntityUid scion = default, node = default;
        var before = 0;
        var fed = 0;

        await server.WaitPost(() =>
        {
            // This predates the garrison, whose guards would frighten the crewman beside the stone and hold the waves back.
            server.CfgMan.SetCVar(WFGarrisonCVars.Enabled, false);
            WFRemadeDirectorTest.LayDeck(pair, source);
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(source, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", WFRemadeDirectorTest.At(source, 3, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await WFRemadeDirectorTest.RunUntil(pair, () => monolith.Corruption.Count >= 4, 90);
        await server.WaitPost(() =>
        {
            // A node on the edge of it feeds tiles of its own.
            var def = server.ResolveDependency<IPrototypeManager>().Index<WFRemadeGrowthPrototype>("WFCorruptionPropagator");
            var edge = monolith.Corruption.Last();
            node = growths.TryBuild(def, entMan.GetComponent<TransformComponent>(edge).Coordinates, scion, ready: true, pay: false) ?? default;
            Assert.That(node, Is.Not.EqualTo(default(EntityUid)), "the node wouldn't grow before the jump.");
        });
        var nodeComp = entMan.GetComponent<WFCorruptionNodeComponent>(node);
        await WFRemadeDirectorTest.RunUntil(pair, () => nodeComp.Tiles.Count >= 1, 60);
        await server.WaitAssertion(() =>
        {
            before = monolith.Corruption.Count;
            fed = nodeComp.Tiles.Count;
            Assert.That(fed, Is.GreaterThanOrEqualTo(1), "the node fed nothing before the jump.");
            Assert.That(director.Seeded, Is.True);
            Assert.That(shuttles.TryAddFTLDestination(target.MapId, true, out _), Is.True);
            shuttles.FTLToCoordinates(shuttle, entMan.GetComponent<ShuttleComponent>(shuttle),
                new EntityCoordinates(target.MapUid, new Vector2(200, 200)), Angle.Zero,
                startupTime: 1f, hyperspaceTime: shuttles.DefaultArrivalTime + 2f);
        });

        await WFRemadeDirectorTest.RunUntil(pair,
            () => entMan.GetComponent<TransformComponent>(shuttle).MapUid == target.MapUid && !entMan.HasComponent<FTLComponent>(shuttle), 120);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(shuttle).MapUid, Is.EqualTo(target.MapUid), "the hull never arrived.");
            Assert.That(entMan.GetComponent<TransformComponent>(scion).GridUid, Is.EqualTo(shuttle), "the Scion didn't make the jump.");
        });

        // Corruption still spreads on the hull in its new place.
        await WFRemadeDirectorTest.RunUntil(pair, () => monolith.Corruption.Count > before + 2, 120);
        await server.WaitAssertion(() =>
        {
            Assert.That(monolith.Corruption.Count, Is.GreaterThan(before + 2), "corruption stopped spreading after the jump.");
            Assert.That(entMan.GetComponent<TransformComponent>(node).GridUid, Is.EqualTo(shuttle), "the node was left behind.");
            Assert.That(nodeComp.Tiles.Count, Is.GreaterThanOrEqualTo(fed), "the node's tiles were lost in the jump.");
            Assert.That(entMan.GetComponent<WFGrowthComponent>(node).Active, Is.True, "the node went dormant after the jump.");
            entMan.SpawnEntity("GasVentPump", WFRemadeDirectorTest.At(source, 20, 0));
            director.Phase = WFRemadeDirectorPhase.Stalk;
            director.PhaseEnd = timing.CurTime;
        });

        // And the Director still plays: it sends a wave in out of sight.
        await WFRemadeDirectorTest.RunUntil(pair, () => director.Horde.Count > 0, 30);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Horde, Is.Not.Empty, "the Director sent nothing after the jump.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALostScionEndsItsOutbreak()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sites = entMan.System<WFRemadeSiteSystem>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = entMan.SpawnEntity("WFMonolithScion", WFRemadeDirectorTest.At(map, 0, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
        });
        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        await WFRemadeDirectorTest.RunUntil(pair, () => monolith.Corruption.Count >= 3, 60);

        List<EntityUid> tiles = new();
        await server.WaitPost(() =>
        {
            tiles = monolith.Corruption.ToList();
            Assert.That(tiles, Is.Not.Empty);
            entMan.DeleteEntity(scion);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(tiles.Where(tile => !entMan.Deleted(tile)).All(tile => entMan.GetComponent<WFCorruptionComponent>(tile).DecayAt != null),
                Is.True, "corruption outlived its Scion.");
            Assert.That(sites.CanStartWild(0f, 1800f, out _), Is.False, "a new Scion followed the lost one at once.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheChoirAreFriendsOfTheRemadeAndHuntWhoeverTakesTheScion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var sites = entMan.System<WFRemadeSiteSystem>();
        var factions = entMan.System<NpcFactionSystem>();
        var objectives = entMan.System<WFCrewObjectiveSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid encounter = default, scion = default, ship = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            var choirist = entMan.SpawnEntity("WFMobChoirist", WFRemadeDirectorTest.At(map, 10, 10));
            var remade = entMan.SpawnEntity("WFRemadeSlasher", WFRemadeDirectorTest.At(map, 14, 10));
            var human = entMan.SpawnEntity("MobHuman", WFRemadeDirectorTest.At(map, 18, 10));
            Assert.That(factions.IsEntityFriendly(choirist, remade), Is.True, "the Choir would shoot the Remade.");
            Assert.That(factions.IsEntityFriendly(remade, choirist), Is.True, "the Remade would tear into the Choir.");
            Assert.That(factions.IsEntityFriendly(choirist, human), Is.False, "the Choir would let a player pass.");
            entMan.DeleteEntity(choirist);
            entMan.DeleteEntity(remade);
            entMan.DeleteEntity(human);

            var site = server.ResolveDependency<IPrototypeManager>().Index<WFRemadeSitePrototype>(Guarded);
            Assert.That(sites.TryStartSite(site, Far(map), false, out encounter), Is.True, "the guarded site didn't start.");
        });
        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.GetComponent<WFRemadeSiteComponent>(encounter).Populated, 90);
        await server.WaitAssertion(() =>
        {
            var site = entMan.GetComponent<WFRemadeSiteComponent>(encounter);
            Assert.That(site.Scion, Is.Not.Null);
            scion = site.Scion!.Value;
            var ships = entMan.GetComponent<WFEncounterComponent>(encounter).Ships;
            ship = ships["choir"].Grid;
            Assert.That(ships["choir"].HasOrders, Is.True);
            Assert.That(entMan.GetComponent<WFEncounterComponent>(encounter).Hidden, Is.False);
            transforms.Unanchor(scion);
            // A player's ship takes the Scion off the hulk.
            transforms.SetCoordinates(scion, WFRemadeDirectorTest.At(map, 6, 0));
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var state = entMan.GetComponent<WFEncounterComponent>(encounter).Ships["choir"];
            var row = objectives.Snapshot().FirstOrDefault(crew => crew.Grid == entMan.GetNetEntity(ship) && crew.Group == state.Group);
            Assert.That(row, Is.Not.Null);
            Assert.That(row!.Objectives.FirstOrDefault()?.Kind, Is.EqualTo(WFCrewObjectiveKind.Attack), "the Choir weren't sent after the carrier.");
            Assert.That(row.Objectives[0].Target, Is.EqualTo(entMan.GetNetEntity(map.Grid.Owner)), "the Choir are after the wrong ship.");
            entMan.GetComponent<WFEncounterComponent>(encounter).Expires = server.ResolveDependency<IGameTiming>().CurTime;
        });

        // Their clock runs out whatever they were told to do, and the hulk and the ship both go.
        EntityUid hulk = default;
        await server.WaitPost(() => hulk = entMan.GetComponent<WFEncounterComponent>(encounter).Ships["hulk"].Grid);
        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(ship) && entMan.Deleted(hulk), 150);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(hulk), Is.True, "the Choir's hulk stayed.");
            Assert.That(entMan.Deleted(ship), Is.True, "the Choir's ship stayed.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PiratesComeForAShipCarryingAScion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var encounters = entMan.System<WFEncounterSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            config.SetCVar(EncountersCVars.Enabled, true);
            config.SetCVar(WFRemadeSectorCVars.HuntInterval, 2f);
            config.SetCVar(WFRemadeSectorCVars.HuntChance, 1f);
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", WFRemadeDirectorTest.At(map, 6, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            // It started somewhere else, so this deck is carrying it.
            entMan.EnsureComponent<WFScionSignalComponent>(scion).Home = map.MapUid;
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => encounters.IsRunning("WFEncounterPirateBoarders"), 90);
        await server.WaitAssertion(() =>
        {
            Assert.That(encounters.IsRunning("WFEncounterPirateBoarders"), Is.True, "no pirates came for the ship with the Scion.");
            var running = entMan.AllEntities<WFEncounterComponent>().Select(c => c.Owner).ToList();
            foreach (var encounter in running)
                encounters.End(encounter);
            config.SetCVar(EncountersCVars.Enabled, EncountersCVars.Enabled.DefaultValue);
            config.SetCVar(WFRemadeSectorCVars.HuntInterval, WFRemadeSectorCVars.HuntInterval.DefaultValue);
            config.SetCVar(WFRemadeSectorCVars.HuntChance, WFRemadeSectorCVars.HuntChance.DefaultValue);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }
}
