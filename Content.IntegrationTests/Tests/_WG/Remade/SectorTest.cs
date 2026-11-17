#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.Encounters;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Encounters;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.Remade.Director;
using Content.Server._WG.Remade.Garrison;
using Content.Server._WG.Remade.Monolith;
using Content.Server._WG.Remade.Structures;
using Content.Server._WG.Remade.Sector;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.Remade;
using Content.Shared._WG.Remade.Sector;
using Content.Shared._WG.Remade.Structures;
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

namespace Content.IntegrationTests.Tests._WG.Remade;

/// <summary>The Remade in the open sector: sites, the discovery rule, the signal, the Choir, infected crews and jumps.</summary>
[TestFixture]
public sealed class SectorTest
{
    private const string Hulk = "WGRemadeSiteHulk";
    private const string Guarded = "WGRemadeSiteGuarded";
    private const string Adrift = "WGRemadeSiteAdrift";

    /// <summary>Where a site's encounter is put on the test map, well clear of its grid and a long way from the player.</summary>
    private static MapCoordinates Far(TestMapData map, float x = 3000f)
    {
        return new MapCoordinates(new Vector2(x, 0f), map.MapId);
    }

    private static List<WGEncounterMarker> Collect(IEntityManager entMan)
    {
        var markers = new List<WGEncounterMarker>();
        var ev = new WGEncounterCollectMarkersEvent(markers);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return markers;
    }

    private static (EntityUid Encounter, EntityUid Scion, EntityUid Grid) SiteOf(IEntityManager entMan, string key)
    {
        var encounter = entMan.AllEntities<WGRemadeSiteComponent>().Select(site => site.Owner).Single();
        var comp = entMan.GetComponent<WGRemadeSiteComponent>(encounter);
        var ships = entMan.GetComponent<WGEncounterComponent>(encounter).Ships;
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
            var keys = new List<string> { "wg-remade-sector-hint-sender", "wg-remade-choir-zone-attack", "wg-remade-signal-adrift" };
            for (var i = 1; i <= 3; i++)
            {
                keys.Add($"wg-remade-sector-hint-{i}");
                keys.Add($"wg-remade-choir-zone-warn-{i}");
            }

            foreach (var stage in Enum.GetValues<WGMonolithStage>())
                keys.Add($"wg-remade-signal-stage-{stage}");

            foreach (var site in proto.EnumeratePrototypes<WGRemadeSitePrototype>())
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

            Assert.That(loc.TryGetString("ent-WGMobChoirist", out var name), Is.True);
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
        var sites = entMan.System<WGRemadeSiteSystem>();
        var encounters = entMan.System<WGEncounterSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var config = server.ResolveDependency<IConfigurationManager>();
        EntityUid rule = default;

        await server.WaitPost(() =>
        {
            Assert.That(sites.Enabled, Is.False, "the sector rules are on without being asked to be.");
            config.SetCVar(EncountersCVars.Enabled, true);
            WGRemadeDirectorTest.SpawnCrewman(pair, WGRemadeDirectorTest.At(map, 0, 0));
            ticker.StartGameRule("WGRemadeDiscoveryRule", out rule);
            var comp = entMan.GetComponent<WGRemadeDiscoveryRuleComponent>(rule);
            comp.Next = TimeSpan.Zero;
            comp.Chance = 1f;
            comp.CheckMin = comp.CheckMax = 1f;
            comp.MinGap = 0f;
            comp.GoneCooldown = 0f;
        });

        // Chance is certain and it rolls every second, so only the one-at-a-time rule can hold the count at one.
        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.AllEntities<WGRemadeSiteComponent>().Any(site => site.Comp.Populated), 90);
        await pair.RunSeconds(15f);

        EntityUid encounter = default, scion = default, grid = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<WGRemadeSiteComponent>().Count(), Is.EqualTo(1), "the rule placed more than one site.");
            Assert.That(entMan.EntityQuery<WGMonolithComponent>().Count(), Is.EqualTo(1), "there is not exactly one Scion in the sector.");
            (encounter, scion, grid) = SiteOf(entMan, "hulk");
            var monolith = entMan.GetComponent<WGMonolithComponent>(scion);
            var signal = entMan.GetComponent<WGScionSignalComponent>(scion);
            Assert.That(monolith.Stage, Is.EqualTo(WGMonolithStage.Quiet), "the discovered Scion isn't dormant.");
            Assert.That(entMan.GetComponent<TransformComponent>(scion).GridUid, Is.EqualTo(grid), "the Scion isn't on the hulk.");
            Assert.That(entMan.GetComponent<TransformComponent>(scion).Anchored, Is.True, "the Scion isn't wrenched down.");
            Assert.That(signal.Wild && !signal.Disturbed, Is.True);

            var director = entMan.GetComponent<WGRemadeDirectorComponent>(scion);
            Assert.That(director.Biomass, Is.Zero, "the Director paid out with nobody near.");
            Assert.That(director.Horde, Is.Empty);
            Assert.That(entMan.EntityQuery<WGRemadeComponent>(), Is.Empty);

            var comp = entMan.GetComponent<WGEncounterComponent>(encounter);
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
            entMan.GetComponent<WGEncounterComponent>(encounter).Expires = timing.CurTime;
        });
        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(grid), 120);
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
        var sites = entMan.System<WGRemadeSiteSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        EntityUid crewman = default, encounter = default;

        await server.WaitPost(() =>
        {
            config.SetCVar(EncountersCVars.Simulate, true);
            config.SetCVar(EncountersCVars.MaterializeInterval, 0.1f);
            crewman = WGRemadeDirectorTest.SpawnCrewman(pair, WGRemadeDirectorTest.At(map, 0, 0));
            var site = server.ResolveDependency<IPrototypeManager>().Index<WGRemadeSitePrototype>(Hulk);
            Assert.That(sites.TryStartSite(site, Far(map, 9000f), false, out encounter), Is.True);
        });
        await pair.RunSeconds(8f);

        // Far off the hull is a point on the map: no Scion exists yet, but one is still counted so no second is placed.
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGRemadeSiteComponent>(encounter).Populated, Is.False);
            Assert.That(entMan.EntityQuery<WGMonolithComponent>(), Is.Empty);
            Assert.That(sites.WildCount(), Is.EqualTo(1), "an unbuilt hulk isn't counted as a wild Scion.");
            Assert.That(sites.CanStartWild(0f, 0f, out _), Is.False);
            transforms.SetCoordinates(crewman, new EntityCoordinates(map.MapUid, new Vector2(7500f, 0f)));
        });

        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.GetComponent<WGRemadeSiteComponent>(encounter).Populated, 90);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGRemadeSiteComponent>(encounter).Populated, Is.True, "the hulk was built and got no Scion.");
            var (_, scion, grid) = SiteOf(entMan, "hulk");
            Assert.That(entMan.GetComponent<TransformComponent>(scion).GridUid, Is.EqualTo(grid));
            Assert.That(entMan.GetComponent<WGEncounterComponent>(encounter).Ships["hulk"].Touched, Is.True, "the hull could go back to simulation and lose its Scion.");
            Assert.That(sites.WildCount(), Is.EqualTo(1));
            entMan.System<WGEncounterSystem>().End(encounter);
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
            WGRemadeDirectorTest.LayDeck(pair, map);
            scion = entMan.SpawnEntity("WGMonolithScion", WGRemadeDirectorTest.At(map, 6, 0));
            entMan.GetComponent<WGRemadeDirectorComponent>(scion).BuildUp = false;
            var signal = entMan.EnsureComponent<WGScionSignalComponent>(scion);
            signal.Wild = true;
            signal.Home = map.Grid.Owner;
            transforms.AnchorEntity(scion);

            // A Scion nobody moved but somebody draws on hard shows all the same.
            drawn = entMan.SpawnEntity("WGMonolithScion", WGRemadeDirectorTest.At(map, -6, 0));
            entMan.GetComponent<WGRemadeDirectorComponent>(drawn).BuildUp = false;
            entMan.GetComponent<WGMonolithComponent>(drawn).Draw = 0.5f;
        });
        await pair.RunSeconds(3f);

        float Range() => Collect(entMan).Single(marker => marker.Encounter == entMan.GetNetEntity(scion)).WarnRange;
        WGEncounterMarker Marker() => Collect(entMan).Single(marker => marker.Encounter == entMan.GetNetEntity(scion));

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
            Assert.That(marker.Category, Is.EqualTo(WGEncounterCategory.Threat));
            entMan.GetComponent<WGMonolithComponent>(scion).Resonance = 20f;
        });
        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            hymn = Range();
            entMan.GetComponent<WGMonolithComponent>(scion).Resonance = 80f;
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
        var received = pair.Client.EntMan.System<WGEncounterMarkerClientSystem>().Markers;
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
            WGRemadeDirectorTest.SpawnCrewman(pair, WGRemadeDirectorTest.At(map, 0, 0));
            console.ExecuteCommand($"wg_remade spawn {Hulk}");
        });

        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.AllEntities<WGRemadeSiteComponent>().Any(site => site.Comp.Populated), 90);

        EntityUid scion = default;
        await server.WaitPost(() =>
        {
            Assert.That(entMan.EntityQuery<WGRemadeSiteComponent>().Count(), Is.EqualTo(1), "wg_remade spawn didn't place the site.");
            scion = SiteOf(entMan, "hulk").Scion;
            Assert.That(entMan.GetComponent<WGMonolithComponent>(scion).Stage, Is.EqualTo(WGMonolithStage.Quiet));
            console.ExecuteCommand("wg_remade resonance 60");
            console.ExecuteCommand("wg_remade status");
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var monolith = entMan.GetComponent<WGMonolithComponent>(scion);
            Assert.That(monolith.Resonance, Is.GreaterThanOrEqualTo(60f), "wg_remade resonance didn't set the wild Scion.");
            Assert.That(monolith.Stage, Is.EqualTo(WGMonolithStage.Harvest));
            Assert.That(entMan.GetComponent<WGScionSignalComponent>(scion).Disturbed, Is.True, "a Scion at Harvest is still silent.");
            var encounter = SiteOf(entMan, "hulk").Encounter;
            entMan.System<WGEncounterSystem>().End(encounter);
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
        var sites = entMan.System<WGRemadeSiteSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        var mobs = entMan.System<MobStateSystem>();
        var inventory = entMan.System<InventorySystem>();
        EntityUid crewman = default, encounter = default, scion = default, grid = default;

        await server.WaitPost(() =>
        {
            crewman = WGRemadeDirectorTest.SpawnCrewman(pair, WGRemadeDirectorTest.At(map, 0, 0));
            // The Director only wakes for a living crewman, and the carp sometimes died under CI load before it got there.
            entMan.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(crewman);
            var site = server.ResolveDependency<IPrototypeManager>().Index<WGRemadeSitePrototype>(Adrift);
            Assert.That(sites.TryStartSite(site, Far(map), false, out encounter), Is.True, "the adrift site didn't start.");
        });
        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.GetComponent<WGRemadeSiteComponent>(encounter).Populated, 60);
        await pair.RunSeconds(3f);

        WGMonolithComponent monolith = default!;
        WGRemadeDirectorComponent director = default!;
        await server.WaitAssertion(() =>
        {
            (_, scion, grid) = SiteOf(entMan, "adrift");
            monolith = entMan.GetComponent<WGMonolithComponent>(scion);
            director = entMan.GetComponent<WGRemadeDirectorComponent>(scion);
            Assert.That(monolith.Stage, Is.EqualTo(WGMonolithStage.Harvest), "the adrift hull isn't deep in an outbreak.");
            Assert.That(entMan.EntityQuery<WGCrewComponent>().Count(), Is.GreaterThan(0), "nobody is left aboard to call for help.");
        });

        // The hull is its own world: corruption spreads over it while nobody looks, and the Director sleeps.
        await WGRemadeDirectorTest.RunUntil(pair, () => monolith.Corruption.Count >= 3, 60);
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
            victim = entMan.AllEntities<WGCrewComponent>().Select(crew => crew.Owner).First();
            gear = inventory.GetHandOrInventoryEntities(victim).ToList();
            Assert.That(gear, Is.Not.Empty, "the crewman carries nothing.");
            var tile = monolith.Corruption.First();
            transforms.SetCoordinates(victim, entMan.GetComponent<TransformComponent>(tile).Coordinates);
            mobs.ChangeMobState(victim, MobState.Dead);
        });
        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.HasComponent<WGRemadeIncubatingComponent>(victim), 30);
        await server.WaitPost(() =>
        {
            Assert.That(entMan.HasComponent<WGRemadeIncubatingComponent>(victim), Is.True, "a crewman dead on corruption never started to turn.");
            entMan.GetComponent<WGRemadeIncubatingComponent>(victim).TurnsAt = TimeSpan.Zero;
        });
        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(victim), 45);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(victim), Is.True, "the crewman never turned.");
            Assert.That(entMan.EntityQuery<WGRemadeComponent>().Any(remade => remade.Source == scion), Is.True, "nothing rose, or it answers to no Monolith.");
            Assert.That(gear.All(item => !entMan.Deleted(item)), Is.True, "the crewman's gear went with him.");
        });

        // With the rest of them dead the ship has no crew and the encounter closes: nothing hangs on a hull that can't be flown.
        await server.WaitPost(() =>
        {
            foreach (var crew in entMan.AllEntities<WGCrewComponent>().Select(c => c.Owner).ToList())
            {
                mobs.ChangeMobState(crew, MobState.Dead);
            }
        });
        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.GetComponent<WGEncounterComponent>(encounter).Resolution != null, 40);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGEncounterComponent>(encounter).Resolution, Is.EqualTo(WGEncounterResolution.Destroyed),
                "an encounter with every hand dead didn't close.");
        });

        // Crew near the Monolith wake the Director.
        await server.WaitPost(() => transforms.SetCoordinates(crewman, entMan.GetComponent<TransformComponent>(scion).Coordinates));
        await WGRemadeDirectorTest.RunUntil(pair, () => director.Seeded, 15);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Seeded, Is.True, $"the Director never woke for the crew: crewman {entMan.GetComponent<Content.Shared.Mobs.Components.MobStateComponent>(crewman).CurrentState}, "
                + $"{(transforms.GetWorldPosition(crewman) - transforms.GetWorldPosition(scion)).Length():F1} m from a {monolith.Stage} stone.");
            Assert.That(director.Biomass, Is.GreaterThan(0f));
            entMan.System<WGEncounterSystem>().End(encounter);
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
        var growths = entMan.System<WGGrowthSystem>();
        var shuttle = source.Grid.Owner;
        EntityUid scion = default, node = default;
        var before = 0;
        var fed = 0;

        await server.WaitPost(() =>
        {
            // This predates the garrison, whose guards would frighten the crewman beside the stone and hold the waves back.
            server.CfgMan.SetCVar(WGGarrisonCVars.Enabled, false);
            WGRemadeDirectorTest.LayDeck(pair, source);
            WGRemadeDirectorTest.SpawnCrewman(pair, WGRemadeDirectorTest.At(source, 0, 0));
            scion = entMan.SpawnEntity("WGMonolithScion", WGRemadeDirectorTest.At(source, 3, 0));
            entMan.GetComponent<WGRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WGMonolithComponent>(scion).Resonance = 40f;
        });

        var monolith = entMan.GetComponent<WGMonolithComponent>(scion);
        var director = entMan.GetComponent<WGRemadeDirectorComponent>(scion);
        await WGRemadeDirectorTest.RunUntil(pair, () => monolith.Corruption.Count >= 4, 90);
        await server.WaitPost(() =>
        {
            // A node on the edge of it feeds tiles of its own.
            var def = server.ResolveDependency<IPrototypeManager>().Index<WGRemadeGrowthPrototype>("WGCorruptionPropagator");
            var edge = monolith.Corruption.Last();
            node = growths.TryBuild(def, entMan.GetComponent<TransformComponent>(edge).Coordinates, scion, ready: true, pay: false) ?? default;
            Assert.That(node, Is.Not.EqualTo(default(EntityUid)), "the node wouldn't grow before the jump.");
        });
        var nodeComp = entMan.GetComponent<WGCorruptionNodeComponent>(node);
        await WGRemadeDirectorTest.RunUntil(pair, () => nodeComp.Tiles.Count >= 1, 60);
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

        await WGRemadeDirectorTest.RunUntil(pair,
            () => entMan.GetComponent<TransformComponent>(shuttle).MapUid == target.MapUid && !entMan.HasComponent<FTLComponent>(shuttle), 120);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(shuttle).MapUid, Is.EqualTo(target.MapUid), "the hull never arrived.");
            Assert.That(entMan.GetComponent<TransformComponent>(scion).GridUid, Is.EqualTo(shuttle), "the Scion didn't make the jump.");
        });

        // Corruption still spreads on the hull in its new place.
        await WGRemadeDirectorTest.RunUntil(pair, () => monolith.Corruption.Count > before + 2, 120);
        await server.WaitAssertion(() =>
        {
            Assert.That(monolith.Corruption.Count, Is.GreaterThan(before + 2), "corruption stopped spreading after the jump.");
            Assert.That(entMan.GetComponent<TransformComponent>(node).GridUid, Is.EqualTo(shuttle), "the node was left behind.");
            Assert.That(nodeComp.Tiles.Count, Is.GreaterThanOrEqualTo(fed), "the node's tiles were lost in the jump.");
            Assert.That(entMan.GetComponent<WGGrowthComponent>(node).Active, Is.True, "the node went dormant after the jump.");
            entMan.SpawnEntity("GasVentPump", WGRemadeDirectorTest.At(source, 20, 0));
            director.Phase = WGRemadeDirectorPhase.Stalk;
            director.PhaseEnd = timing.CurTime;
        });

        // And the Director still plays: it sends a wave in out of sight.
        await WGRemadeDirectorTest.RunUntil(pair, () => director.Horde.Count > 0, 30);
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
        var sites = entMan.System<WGRemadeSiteSystem>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            WGRemadeDirectorTest.LayDeck(pair, map);
            scion = entMan.SpawnEntity("WGMonolithScion", WGRemadeDirectorTest.At(map, 0, 0));
            entMan.GetComponent<WGRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WGMonolithComponent>(scion).Resonance = 40f;
        });
        var monolith = entMan.GetComponent<WGMonolithComponent>(scion);
        await WGRemadeDirectorTest.RunUntil(pair, () => monolith.Corruption.Count >= 3, 60);

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
            Assert.That(tiles.Where(tile => !entMan.Deleted(tile)).All(tile => entMan.GetComponent<WGCorruptionComponent>(tile).DecayAt != null),
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
        var sites = entMan.System<WGRemadeSiteSystem>();
        var factions = entMan.System<NpcFactionSystem>();
        var objectives = entMan.System<WGCrewObjectiveSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid encounter = default, scion = default, ship = default;

        await server.WaitPost(() =>
        {
            WGRemadeDirectorTest.LayDeck(pair, map);
            WGRemadeDirectorTest.SpawnCrewman(pair, WGRemadeDirectorTest.At(map, 0, 0));
            var choirist = entMan.SpawnEntity("WGMobChoirist", WGRemadeDirectorTest.At(map, 10, 10));
            var remade = entMan.SpawnEntity("WGRemadeSlasher", WGRemadeDirectorTest.At(map, 14, 10));
            var human = entMan.SpawnEntity("MobHuman", WGRemadeDirectorTest.At(map, 18, 10));
            Assert.That(factions.IsEntityFriendly(choirist, remade), Is.True, "the Choir would shoot the Remade.");
            Assert.That(factions.IsEntityFriendly(remade, choirist), Is.True, "the Remade would tear into the Choir.");
            Assert.That(factions.IsEntityFriendly(choirist, human), Is.False, "the Choir would let a player pass.");
            entMan.DeleteEntity(choirist);
            entMan.DeleteEntity(remade);
            entMan.DeleteEntity(human);

            var site = server.ResolveDependency<IPrototypeManager>().Index<WGRemadeSitePrototype>(Guarded);
            Assert.That(sites.TryStartSite(site, Far(map), false, out encounter), Is.True, "the guarded site didn't start.");
        });
        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.GetComponent<WGRemadeSiteComponent>(encounter).Populated, 90);
        await server.WaitAssertion(() =>
        {
            var site = entMan.GetComponent<WGRemadeSiteComponent>(encounter);
            Assert.That(site.Scion, Is.Not.Null);
            scion = site.Scion!.Value;
            var ships = entMan.GetComponent<WGEncounterComponent>(encounter).Ships;
            ship = ships["choir"].Grid;
            Assert.That(ships["choir"].HasOrders, Is.True);
            Assert.That(entMan.GetComponent<WGEncounterComponent>(encounter).Hidden, Is.False);
            transforms.Unanchor(scion);
            // A player's ship takes the Scion off the hulk.
            transforms.SetCoordinates(scion, WGRemadeDirectorTest.At(map, 6, 0));
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var state = entMan.GetComponent<WGEncounterComponent>(encounter).Ships["choir"];
            var row = objectives.Snapshot().FirstOrDefault(crew => crew.Grid == entMan.GetNetEntity(ship) && crew.Group == state.Group);
            Assert.That(row, Is.Not.Null);
            Assert.That(row!.Objectives.FirstOrDefault()?.Kind, Is.EqualTo(WGCrewObjectiveKind.Attack), "the Choir weren't sent after the carrier.");
            Assert.That(row.Objectives[0].Target, Is.EqualTo(entMan.GetNetEntity(map.Grid.Owner)), "the Choir are after the wrong ship.");
            entMan.GetComponent<WGEncounterComponent>(encounter).Expires = server.ResolveDependency<IGameTiming>().CurTime;
        });

        // Their clock runs out whatever they were told to do, and the hulk and the ship both go.
        EntityUid hulk = default;
        await server.WaitPost(() => hulk = entMan.GetComponent<WGEncounterComponent>(encounter).Ships["hulk"].Grid);
        await WGRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(ship) && entMan.Deleted(hulk), 150);
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
        var encounters = entMan.System<WGEncounterSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            config.SetCVar(EncountersCVars.Enabled, true);
            config.SetCVar(WGRemadeSectorCVars.HuntInterval, 2f);
            config.SetCVar(WGRemadeSectorCVars.HuntChance, 1f);
            WGRemadeDirectorTest.LayDeck(pair, map);
            WGRemadeDirectorTest.SpawnCrewman(pair, WGRemadeDirectorTest.At(map, 0, 0));
            scion = entMan.SpawnEntity("WGMonolithScion", WGRemadeDirectorTest.At(map, 6, 0));
            entMan.GetComponent<WGRemadeDirectorComponent>(scion).BuildUp = false;
            // It started somewhere else, so this deck is carrying it.
            entMan.EnsureComponent<WGScionSignalComponent>(scion).Home = map.MapUid;
        });

        await WGRemadeDirectorTest.RunUntil(pair, () => encounters.IsRunning("WGEncounterPirateBoarders"), 90);
        await server.WaitAssertion(() =>
        {
            Assert.That(encounters.IsRunning("WGEncounterPirateBoarders"), Is.True, "no pirates came for the ship with the Scion.");
            var running = entMan.AllEntities<WGEncounterComponent>().Select(c => c.Owner).ToList();
            foreach (var encounter in running)
                encounters.End(encounter);
            config.SetCVar(EncountersCVars.Enabled, EncountersCVars.Enabled.DefaultValue);
            config.SetCVar(WGRemadeSectorCVars.HuntInterval, WGRemadeSectorCVars.HuntInterval.DefaultValue);
            config.SetCVar(WGRemadeSectorCVars.HuntChance, WGRemadeSectorCVars.HuntChance.DefaultValue);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }
}
