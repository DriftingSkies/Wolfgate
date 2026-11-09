#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.RoundSetup;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Cargo.Components;
using Content.Shared.CCVar;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>The ready-made outposts: each one works on landing and costs at least what a save of it would.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostPresetPrototype))]
public sealed class OutpostPresetTest
{
    /// <summary>A tile of outpost floor appraises at what one foundation plate is worth, not a whole stack of them.</summary>
    [Test]
    public async Task FloorAppraisesPerPlate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var plate = server.ProtoMan.Index<EntityPrototype>("WFOutpostFoundationPlate");
            Assert.That(plate.TryGetComponent<StackPriceComponent>(out var price, server.ResolveDependency<IComponentFactory>()),
                "Precondition: a plate has a per-unit price.");
            Assert.That(server.System<WFOutpostSystem>().PlatePrice(), Is.EqualTo(price!.Price).Within(0.01));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every preset grid loads with one console, a pod, a trade hub under open sky, a water and a food synthesizer and a
    /// starter crate of materials, within the save caps, and its price
    /// is at least the load price of its appraisal.
    /// </summary>
    [Test]
    public async Task EveryPresetIsAWorkingOutpost()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var loader = server.System<MapLoaderSystem>();
        var maps = server.System<SharedMapSystem>();
        var roofs = server.System<SharedRoofSystem>();
        var transform = server.System<SharedTransformSystem>();
        var outposts = server.System<WFOutpostSystem>();
        var presets = protos.EnumeratePrototypes<WFOutpostPresetPrototype>().ToList();
        Assert.That(presets, Is.Not.Empty);

        await server.WaitAssertion(() => Assert.Multiple(() =>
        {
            foreach (var preset in presets)
            {
                maps.CreateMap(out var mapId);
                Assert.That(loader.TryLoadGrid(mapId, preset.Grid, out var loaded), Is.True, $"{preset.ID} didn't load.");
                var grid = loaded!.Value;
                var on = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
                var consoles = 0;
                var pods = 0;
                var hubs = 0;
                var water = 0;
                var food = 0;
                var starter = 0;
                while (on.MoveNext(out var child))
                {
                    if (entMan.HasComponent<WFOutpostConsoleComponent>(child))
                        consoles++;
                    if (entMan.TryGetComponent<WFReagentSynthesizerComponent>(child, out var synth))
                    {
                        if (synth.Reagent == "Water")
                            water++;
                        if (synth.Reagent == "Nutriment")
                            food++;
                    }
                    if (entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == "WFCrateOutpostStarterMaterials")
                        starter++;
                    if (entMan.HasComponent<WFOutpostCryopodComponent>(child))
                        pods++;
                    if (!entMan.HasComponent<WFUniversalTradeHubComponent>(child))
                        continue;

                    var tile = maps.TileIndicesFor(grid, grid.Comp, transform.GetMapCoordinates(child));
                    var roofed = entMan.TryGetComponent<RoofComponent>(grid, out var roof) && roofs.IsRooved((grid, grid.Comp, roof), tile);
                    Assert.That(roofed, Is.False, $"{preset.ID}'s trade hub is under a roof, where nothing can drop on it.");
                    hubs++;
                }

                Assert.That(consoles, Is.EqualTo(1), $"{preset.ID} has {consoles} consoles.");
                Assert.That(pods, Is.GreaterThan(0), $"{preset.ID} has no pod.");
                Assert.That(hubs, Is.GreaterThan(0), $"{preset.ID} has no trade hub.");
                Assert.That(water, Is.GreaterThan(0), $"{preset.ID} has no water synthesizer.");
                Assert.That(food, Is.GreaterThan(0), $"{preset.ID} has no sustenance dispenser.");
                Assert.That(starter, Is.EqualTo(1), $"{preset.ID} has no starter materials crate.");
                Assert.That(outposts.CountTiles(grid), Is.LessThanOrEqualTo(server.CfgMan.GetCVar(OutpostCVars.MaxTiles)), preset.ID);
                Assert.That(outposts.CountEntities(grid), Is.LessThanOrEqualTo(server.CfgMan.GetCVar(OutpostCVars.MaxEntities)), preset.ID);

                var appraisal = outposts.Appraise(grid);
                Assert.That(preset.Price, Is.GreaterThanOrEqualTo(WFOutpostSystem.LoadPrice(appraisal)),
                    $"{preset.ID} appraises at {appraisal}, so it must cost at least {WFOutpostSystem.LoadPrice(appraisal)}.");
                maps.DeleteMap(mapId);
            }
        }));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The homestead brought down in place of a freshly founded outpost lands round the founding console's tile, as no
    /// save slot, for its listed price; a second preset on the built outpost is refused.
    /// </summary>
    [Test]
    public async Task APresetLandsOnAFoundedOutpost()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var transform = server.System<SharedTransformSystem>();
        var bank = server.System<BankSystem>();
        var owner = pair.Player!.UserId;
        var preset = server.ResolveDependency<IPrototypeManager>().Index<WFOutpostPresetPrototype>("WFOutpostPresetHomestead");

        var funding = Task.CompletedTask;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(OutpostCVars.LoadDelay, 1f);
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var p = prefs.GetPreferences(owner);
            var profile = (HumanoidCharacterProfile) p.SelectedCharacter;
            funding = prefs.SetProfile(owner, p.SelectedCharacterIndex, profile.WithBankBalance(1_000_000));
            entMan.EnsureComponent<BankAccountComponent>(user);
        });
        await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);

        int Balance()
        {
            bank.TryGetBalance(pair.Player!, out var balance);
            return balance;
        }

        try
        {
            EntityUid founded = default;
            Vector2 consoleAt = default;
            var before = 0;
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                founded = grid!.Value;
                var outpost = new Entity<WFOutpostComponent>(founded, entMan.GetComponent<WFOutpostComponent>(founded));
                consoleAt = transform.GetWorldPosition(outpost.Comp.Console);
                before = Balance();
                Assert.That(outposts.TryStartPresetLoad(user, outpost, "WFOutpostPresetNowhere", out reason), Is.False);
                Assert.That(reason, Is.EqualTo("wf-outpost-preset-unknown"));
                Assert.That(outposts.TryStartPresetLoad(user, outpost, preset.ID, out reason), Is.True, $"The load was refused: {reason}.");
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<WFOutpostPendingLoadComponent>(founded), "The outline is drawn on the founding outpost.");
                Assert.That(Balance(), Is.EqualTo(before - preset.Price), "The preset's listed price is charged.");
            });

            await pair.RunTicksSync(90);

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.Deleted(founded), "The founding outpost is gone.");
                Assert.That(outposts.TryGetOutpost(owner, out var loaded), Is.True, "The preset is the owner's outpost.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(loaded).EntityName, Is.EqualTo(server.ResolveDependency<ILocalizationManager>().GetString(preset.Name)));
                Assert.That(transform.GetWorldPosition(loaded.Comp.Console), Is.EqualTo(consoleAt).Using<Vector2>((a, b) => (a - b).Length() < 0.01f),
                    "The console landed on the founding console's tile.");
                Assert.That(entMan.GetComponent<WFOutpostConsoleComponent>(loaded.Comp.Console).Outpost, Is.EqualTo(loaded.Owner));
                Assert.That(loaded.Comp.Slot, Is.Null, "A preset fills no save slot.");
                Assert.That(Balance(), Is.EqualTo(before - preset.Price), "The load was charged once.");

                Assert.That(outposts.TryStartPresetLoad(user, loaded, preset.ID, out var reason), Is.False, "A built outpost takes no preset.");
                Assert.That(reason, Is.EqualTo("wf-outpost-load-not-fresh"));
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An Outpost crew picks the homestead instead of a save: the host pays its listed price at launch and wakes on it,
    /// landed under the preset's name as no save slot.
    /// </summary>
    [Test]
    public async Task AnOutpostCrewLaunchesWithAPreset()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var setup = server.System<WFRoundSetupSystem>();
        var bank = server.System<BankSystem>();
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var onRequest = typeof(WFRoundSetupSystem).GetMethod("OnRequest", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var preset = server.ResolveDependency<IPrototypeManager>().Index<WFOutpostPresetPrototype>("WFOutpostPresetHomestead");
        var host = pair.Player!;
        var hostSlot = prefs.GetPreferences(host.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) prefs.GetPreferences(host.UserId).SelectedCharacter!;
        var roleTimers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        List<EntityUid>? layers = null;
        const int funds = 1_000_000;

        try
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.GameRoleTimers, false));
            await EnableFeature(pair);
            await server.WaitPost(() => server.CfgMan.SetCVar(OutpostCVars.Enabled, true));
            (layers, _, _) = await BuildOwnedStack(pair);

            var funding = Task.CompletedTask;
            await server.WaitPost(() =>
            {
                var profile = (HumanoidCharacterProfile) prefs.GetPreferences(host.UserId).SelectedCharacter;
                funding = prefs.SetProfile(host.UserId, hostSlot, profile.WithoutAllTraitPreferences().WithBankBalance(funds));
            });
            await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);

            await Act(new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Outpost });
            var id = LobbyField<int>("Id");
            await Act(new WFRoundSetupRequest(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = WFOutpostLimits.SettlerJob });
            await Act(new WFRoundSetupRequest(WFRoundSetupAction.SelectOutpost) { LobbyId = id, OutpostPreset = preset.ID });
            Assert.That(LobbyField<string?>("OutpostPreset"), Is.EqualTo(preset.ID));
            Assert.That(LobbyField<int>("OutpostPrice"), Is.EqualTo(preset.Price));
            await Act(new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin)
            {
                LobbyId = id, PaymentRevision = LobbyField<int>("PaymentRevision"),
            });

            await server.WaitPost(() => ticker.StartRound(force: true));
            for (var i = 0; i < 30 && host.AttachedEntity == null; i++)
                await pair.RunTicksSync(pair.SecondsToTicks(1f));

            await server.WaitAssertion(() =>
            {
                Assert.That(host.AttachedEntity, Is.Not.Null, "The host never woke at their outpost.");
                var grid = entMan.GetComponent<TransformComponent>(host.AttachedEntity!.Value).GridUid!.Value;
                Assert.That(entMan.TryGetComponent<WFOutpostComponent>(grid, out var outpost), Is.True, "The host didn't wake on an outpost.");
                Assert.That(outpost!.Owner, Is.EqualTo(host.UserId));
                Assert.That(outpost.Slot, Is.Null, "A preset fills no save slot.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(grid).EntityName,
                    Is.EqualTo(server.ResolveDependency<ILocalizationManager>().GetString(preset.Name)));
                Assert.That(bank.TryGetBalance(host, out var balance), Is.True);
                Assert.That(balance, Is.EqualTo(funds - preset.Price), "The host paid the preset's price once.");
            });
        }
        finally
        {
            if (layers != null)
                await Teardown(pair, layers);
            await server.WaitPost(() =>
            {
                prefs.SetProfile(host.UserId, hostSlot, original).Wait();
                ticker.RestartRound();
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, roleTimers);
            });
            await pair.RunTicksSync(2);
        }

        await pair.CleanReturnAsync();

        async Task Act(WFRoundSetupRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => onRequest.Invoke(setup, new object[] { request, new EntitySessionEventArgs(host) }));
            await pair.RunTicksSync(1);
        }

        T LobbyField<T>(string field)
        {
            var lobbies = (IDictionary) typeof(WFRoundSetupSystem)
                .GetField("_lobbies", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(setup)!;
            foreach (var lobby in lobbies.Values)
            {
                if ((NetUserId) lobby!.GetType().GetField("Host")!.GetValue(lobby)! == host.UserId)
                    return (T) lobby.GetType().GetField(field)!.GetValue(lobby)!;
            }

            Assert.Fail("The host leads no crew.");
            return default!;
        }
    }
}
