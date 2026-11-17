using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WG.Companies;
using Content.Client._WG.RoundSetup;
using Content.Client._WG.ShipPreview;
using Content.Client._WG.ShipPreview.UI;
using Content.IntegrationTests.Tests._WG.RoundSetup;
using Content.Server._NF.SectorServices;
using Content.Server._WG.Companies;
using Content.Server._WG.RoundSetup;
using Content.Server._WG.Drydock;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Voting.Managers;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Companies;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.RoundSetup;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Companies;

public sealed partial class WGCompanyTest
{
    private static readonly ProtoId<VesselPrototype> GuppyProto = "Guppy";

    /// <summary>A licence launches its ship through Crew Setup once per round, charging the registry and launch fees.</summary>
    [Test]
    public async Task HangarLicencesLaunchCompanyShips()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var companies = server.System<WGCompanySystem>();
        var client = pair.Client.System<WGCompanyClientSystem>();
        var crews = pair.Client.System<WGRoundSetupClientSystem>();
        var ticker = server.System<GameTicker>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var owner = pair.Player!;
        var preset = ticker.Preset;
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter;
        var slot = preferences.GetPreferences(owner.UserId).SelectedCharacterIndex;
        var guppy = server.ResolveDependency<IPrototypeManager>().Index<VesselPrototype>(GuppyProto).Price;
        int? id = null;
        var hands = await server.AddDummySessions(2);
        var roundSetup = server.System<WGRoundSetupSystem>();
        var onRequest = typeof(WGRoundSetupSystem).GetMethod("OnRequest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
                foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                    vote.Cancel();
                preferences.SetProfile(owner.UserId, slot, original.WithName("Ada Owner").WithBankBalance(5000)).Wait();
            });
            for (var i = 0; i < 300 && !companies.Loaded; i++)
                await pair.RunTicksSync(1);
            var state = await Send(new(WGCompanyAction.Create) { Name = "Hangar Works", Tag = "HNGR" });
            id = state.Own!.Id;
            await server.WaitPost(() => companies.AdminAdjust(id.Value, WGCompanyPolicy.LicencePrice(guppy) + 100000, "Test", "seed"));
            state = await Send(new(WGCompanyAction.SetLaunchFee) { CompanyId = id.Value, Amount = 250 });
            Assert.That(state.Own!.LicenceOffers.Any(offer => offer.Vessel == "Guppy"), Is.True);
            var vessels = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();
            Assert.That(state.Own.LicenceOffers.Any(offer => vessels.Index<Content.Shared._NF.Shipyard.Prototypes.VesselPrototype>(offer.Vessel).Access == "Mercenary"),
                Is.True, "Companies can license Mercenary designs civilians can't buy.");
            state = await Send(new(WGCompanyAction.BuyLicence) { CompanyId = id.Value, Vessel = "Guppy" });
            Assert.That(state.Own!.Licences, Is.Empty, "A one-person company can't buy fleet licences.");
            for (var i = 0; i < 300 && !hands.All(hand => preferences.HavePreferencesLoaded(hand)); i++)
                await pair.RunTicksSync(1);
            foreach (var hand in hands)
            {
                await Send(new(WGCompanyAction.Invite) { CompanyId = id.Value, TargetUser = hand.UserId });
                await Run(() => companies.HandleRequest(new(WGCompanyAction.AcceptInvite) { CompanyId = id.Value }, hand));
            }
            state = await Send(new(WGCompanyAction.Refresh));
            Assert.That(state.Own!.Members, Has.Length.EqualTo(3));
            state = await Send(new(WGCompanyAction.BuyLicence) { CompanyId = id.Value, Vessel = "Guppy" });
            Assert.That(state.Own!.Licences.Single().Vessel, Is.EqualTo("Guppy"), state.Message);
            Assert.That(state.Own.Balance, Is.EqualTo(100000), "The licence price comes out of the account.");
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            state = await Send(new(WGCompanyAction.Refresh));
            var licence = state.Own!.Licences.Single().Id;
            Assert.That(licence, Is.Not.Zero);

            // The ship's name is kept with the licence and used for every launch.
            state = await Send(new(WGCompanyAction.SetLicenceName) { CompanyId = id.Value, LicenceId = licence, Name = "Long Haul" });
            Assert.That(state.Own!.Licences.Single().ShipName, Is.EqualTo("Long Haul"), state.Message);

            // The quick join card offers the ship by that name, in one click.
            await Crew(new(WGRoundSetupAction.Refresh));
            await pair.Client.WaitAssertion(() =>
            {
                var card = new WGCompanyQuickJoin(true);
                var root = pair.Client.ResolveDependency<Robust.Client.UserInterface.IUserInterfaceManager>().WindowRoot;
                root.AddChild(card);
                var buttons = card.GetChild(0).GetChild(0).Children.OfType<Robust.Client.UserInterface.Controls.Button>().Select(button => button.Text).ToArray();
                root.RemoveChild(card);
                Assert.That(buttons, Does.Contain("Launch Long Haul"));
            });

            // A crew flying under the host's company drops it when the host switches to a character outside it.
            var clientPrefs = pair.Client.ResolveDependency<Content.Client.Lobby.IClientPreferencesManager>();
            var created = await Crew(new(WGRoundSetupAction.Create) { Scenario = WGRoundScenario.Ship, CompanyId = id.Value, Title = "Switchers" });
            var switcher = created.Lobbies.Single(lobby => lobby.Id == created.OwnLobby);
            Assert.That(switcher.CompanyId, Is.EqualTo(id.Value), created.Message);
            Assert.That(switcher.Members.Single().RankLocked, Is.True);
            await pair.Client.WaitPost(() =>
            {
                clientPrefs.UpdateCharacter(original.WithName("Bo Outsider"), slot + 1);
                clientPrefs.SelectCharacter(slot + 1);
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            var switched = await Crew(new(WGRoundSetupAction.Refresh));
            var dropped = switched.Lobbies.Single(lobby => lobby.Id == switched.OwnLobby);
            Assert.Multiple(() =>
            {
                Assert.That(switched.OwnCompany, Is.Null);
                Assert.That(dropped.CompanyId, Is.Null);
                Assert.That(dropped.Members.Single().RankLocked, Is.False);
                Assert.That(dropped.Members.Single().Job, Is.Null);
            });
            await pair.Client.WaitPost(() =>
            {
                clientPrefs.SelectCharacter(slot);
                clientPrefs.DeleteCharacter(slot + 1);
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await Crew(new(WGRoundSetupAction.Leave) { LobbyId = switched.OwnLobby!.Value });

            // Starting a round reads the database off the main thread, which the synchronous test database refuses mid-request.
            await Run(() => companies.Flush());
            await server.WaitPost(() =>
            {
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
                if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
                    em.AddComponent<StationSectorServiceHostComponent>(em.SpawnEntity(null, MapCoordinates.Nullspace));
            });
            state = await Send(new(WGCompanyAction.Launch) { CompanyId = id.Value, LicenceId = licence });
            Assert.That(state.Own!.Licences.Single().Launched, Is.True, state.Message);
            var crewState = await Crew(new(WGRoundSetupAction.Refresh));
            var crew = crewState.Lobbies.Single(lobby => lobby.Id == crewState.OwnLobby);
            Assert.Multiple(() =>
            {
                Assert.That(crew.Hangar, Is.True);
                Assert.That(crew.Vessel, Is.EqualTo("Guppy"));
                Assert.That(crew.CompanyTag, Is.EqualTo("HNGR"));
                Assert.That(crew.ShipName, Is.EqualTo("Long Haul"));
                Assert.That(crew.Access, Is.EqualTo(WGLobbyAccess.CompanyFirst));
                Assert.That(crewState.Payment, Is.Null, "The crew pays nothing for a hangar ship.");
            });
            // A company member joins the company crew as their rank: no opening, the rank's job and title.
            await Crew(new(WGRoundSetupAction.Configure) { LobbyId = crew.Id, Access = WGLobbyAccess.CompanyOnly, AllowLateJoin = true });
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => onRequest.Invoke(roundSetup, new object[]
                { new WGRoundSetupRequest(WGRoundSetupAction.Join) { LobbyId = crew.Id, AsRank = true }, new EntitySessionEventArgs(hands[0]) }));
            crewState = await Crew(new(WGRoundSetupAction.Refresh));
            var hand0 = crewState.Lobbies.Single(lobby => lobby.Id == crew.Id).Members.Single(member => member.UserId == hands[0].UserId);
            Assert.Multiple(() =>
            {
                Assert.That(hand0.RoleId, Is.Null);
                Assert.That(hand0.Title, Is.EqualTo("Recruit"));
                Assert.That(hand0.Job, Is.EqualTo("Contractor"));
            });
            // Everyone from the company in a company crew plays their rank, and can't pick another job.
            var ownerRank = state.Own!.OwnerRank!.Title;
            crewState = await Crew(new(WGRoundSetupAction.SelectJob) { LobbyId = crew.Id, Job = "Pilot" });
            var lockedCrew = crewState.Lobbies.Single(lobby => lobby.Id == crew.Id);
            var lockedHost = lockedCrew.Members.Single(member => member.UserId == owner.UserId);
            Assert.Multiple(() =>
            {
                Assert.That(lockedHost.RankLocked, Is.True);
                Assert.That(lockedHost.Job, Is.EqualTo("Contractor"));
                Assert.That(lockedHost.Title, Is.EqualTo(ownerRank));
                Assert.That(lockedHost.RankId, Is.Not.Zero, "The crew list draws the rank's icon.");
                Assert.That(lockedCrew.HostRankId, Is.Not.Zero);
                Assert.That(lockedCrew.Members.Single(member => member.UserId == hands[0].UserId).RankId, Is.Not.Zero);
            });
            WGCompanyProfileEvent profile = null;
            await pair.Client.WaitPost(() =>
            {
                client.ProfileReceived += received => profile = received;
                client.Send(new WGCompanyRequest(WGCompanyAction.RequestProfile) { CompanyId = id.Value });
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            Assert.That(profile?.Ships.Single().Name, Is.EqualTo("Long Haul"), "The profile lists what's in the hangar.");
            crewState = await Crew(new(WGRoundSetupAction.ConfirmJoin) { LobbyId = crew.Id });
            Assert.That(crewState.Lobbies.Single(lobby => lobby.Id == crew.Id).Started, Is.True, crewState.Message);
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            await server.WaitAssertion(() =>
            {
                var registry = WGCompanyPolicy.RegistryFee(guppy, 0);
                Assert.That(companies.GetBalance(id.Value), Is.EqualTo(100000 - registry + 250), "Registry fee out, launch fee in.");
                Assert.That(((HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter).BankBalance, Is.EqualTo(4750));
                var grid = em.GetComponent<TransformComponent>(owner.AttachedEntity!.Value).GridUid!.Value;
                Assert.That(em.GetComponent<WGCompanyHangarShipComponent>(grid).CompanyId, Is.EqualTo(id.Value));
                Assert.That(em.GetComponent<WGCompanyShipComponent>(grid).Tag, Is.EqualTo("HNGR"));
                Assert.That(server.System<Content.Shared.Access.Systems.SharedIdCardSystem>().TryFindIdCard(owner.AttachedEntity!.Value, out var ownerCard), Is.True);
                Assert.That(ownerCard.Comp.LocalizedJobTitle, Is.EqualTo(ownerRank), "A member in the overflow job shows their rank, not Vagrant.");

                // Breaking it or stripping it for sale pays nothing.
                var console = em.SpawnEntity(null, MapCoordinates.Nullspace);
                var drydock = em.AddComponent<WGDrydockConsoleComponent>(console);
                Assert.That(server.System<WGDrydockSystem>().BreakRefusal(console, drydock, grid), Is.Not.Null);
                em.DeleteEntity(console);
                var pricing = server.System<PricingSystem>();
                var properties = 0;
                var query = em.EntityQueryEnumerator<WGCompanyPropertyComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var xform))
                {
                    if (xform.GridUid != grid)
                        continue;
                    properties++;
                    Assert.That(pricing.GetPrice(uid, includeContents: false), Is.Zero, $"{em.ToPrettyString(uid)} sells for nothing.");
                }
                Assert.That(properties, Is.GreaterThan(10));
                var loose = em.SpawnEntity("SheetSteel", new EntityCoordinates(grid, default));
                Assert.That(pricing.GetPrice(loose), Is.GreaterThan(0), "Things brought aboard later keep their value.");
            });
            state = await Send(new(WGCompanyAction.Launch) { CompanyId = id.Value, LicenceId = licence });
            Assert.That(state.Own!.Licences.Single().Launched, Is.True);
            Assert.That(state.Own.LaunchedThisRound, Is.EqualTo(1));

            // The ship wears the company colour, and Company access lets members through.
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            await server.WaitAssertion(() =>
            {
                var body = owner.AttachedEntity!.Value;
                var grid = em.GetComponent<TransformComponent>(body).GridUid!.Value;
                Assert.That(em.GetComponent<Content.Shared.Shuttles.Components.IFFComponent>(grid).Color,
                    Is.EqualTo(em.GetComponent<WGCompanyShipComponent>(grid).Colour));
                var access = server.System<Content.Server._WG.ShipAccess.WGShipAccessServerSystem>();
                var ship = new Entity<Content.Shared._WG.ShipAccess.WGShipAccessComponent>(grid, em.GetComponent<Content.Shared._WG.ShipAccess.WGShipAccessComponent>(grid));
                Assert.That(access.TryLinkCompany(ship, body), Is.True);
                Assert.That(access.SetMode(ship, Content.Shared._WG.ShipAccess.WGShipAccessMode.Company), Is.True);
                Assert.That(ship.Comp.CompanyName, Is.EqualTo("Hangar Works"));
                Assert.That(ship.Comp.CompanyKeys, Is.Not.Empty, "The owner's issued ID is a company key.");
            });
        }
        finally
        {
            if (id is { } left)
                await server.WaitPost(() => companies.AdminDisband(left));
            // The synchronous test database takes one request at a time: let company writes land, save the profile, then restart.
            await Run(() => companies.Flush());
            await server.WaitPost(() => preferences.SetProfile(owner.UserId, slot, original).Wait());
            await server.WaitPost(() =>
            {
                ticker.RestartRound();
                ticker.SetGamePreset(preset);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote);
            });
            await Run(() => companies.Flush());
            await server.RemoveAllDummySessions();
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        async Task<WGCompanyState> Send(WGCompanyRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await pair.Client.WaitPost(() => client.Send(request));
            await pair.RunTicksSync(pair.SecondsToTicks(0.75f));
            WGCompanyState result = null;
            await pair.Client.WaitPost(() => result = client.State);
            return result;
        }

        async Task Run(System.Func<Task> start)
        {
            Task task = null;
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => task = start());
            for (var i = 0; i < 600 && !task.IsCompleted; i++)
                await pair.RunTicksSync(1);
            await task;
        }

        async Task<WGRoundSetupState> Crew(WGRoundSetupRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await pair.Client.WaitPost(() => crews.Send(request));
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            WGRoundSetupState result = null;
            await pair.Client.WaitPost(() => result = crews.State);
            return result;
        }
    }

    /// <summary>Preview shows a held licence and the design being bought, follows the picker, and closes with the window.</summary>
    [Test]
    public async Task HangarPreviewShowsLicencesAndOffers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var companies = server.System<WGCompanySystem>();
        var client = pair.Client.System<WGCompanyClientSystem>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var maps = pair.Client.System<SharedMapSystem>();
        var owner = pair.Player!;
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter;
        var slot = preferences.GetPreferences(owner.UserId).SelectedCharacterIndex;
        var guppy = server.ResolveDependency<IPrototypeManager>().Index<VesselPrototype>(GuppyProto);
        int? id = null;
        var hands = await server.AddDummySessions(2);
        try
        {
            await server.WaitPost(() => preferences.SetProfile(owner.UserId, slot, original.WithName("Ada Owner").WithBankBalance(5000)).Wait());
            for (var i = 0; i < 300 && !(companies.Loaded && hands.All(hand => preferences.HavePreferencesLoaded(hand))); i++)
                await pair.RunTicksSync(1);
            var state = await Send(new(WGCompanyAction.Create) { Name = "Preview Works", Tag = "PREV" });
            id = state.Own!.Id;
            await server.WaitPost(() => companies.AdminAdjust(id.Value, WGCompanyPolicy.LicencePrice(guppy.Price), "Test", "seed"));
            foreach (var hand in hands)
            {
                await Send(new(WGCompanyAction.Invite) { CompanyId = id.Value, TargetUser = hand.UserId });
                await Run(() => companies.HandleRequest(new(WGCompanyAction.AcceptInvite) { CompanyId = id.Value }, hand));
            }
            await Send(new(WGCompanyAction.Refresh));
            state = await Send(new(WGCompanyAction.BuyLicence) { CompanyId = id.Value, Vessel = "Guppy" });
            Assert.That(state.Own!.Licences.Single().Vessel, Is.EqualTo("Guppy"), state.Message);
            var offers = state.Own.LicenceOffers;
            Assert.That(offers, Has.Length.GreaterThan(1));

            HashSet<MapId> originalMaps = null!;
            WGCompanyWindow window = null!;
            await pair.Client.WaitPost(() =>
            {
                originalMaps = maps.GetAllMapIds().ToHashSet();
                window = new WGCompanyWindow();
                window.OpenCentered();
            });
            await pair.RunTicksSync(pair.SecondsToTicks(1f));

            Button licencePreview = null!;
            Button offerPreview = null!;
            OptionButton picker = null!;
            await pair.Client.WaitAssertion(() =>
            {
                var text = Loc.GetString("wg-company-hangar-preview");
                var tabs = Descendants(window).OfType<TabContainer>().Single();
                for (var i = 0; i < tabs.ChildCount; i++)
                {
                    if (Descendants(tabs.GetChild(i)).OfType<Button>().Any(button => button.Text == text))
                        tabs.CurrentTab = i;
                }
                var buttons = Descendants(tabs).OfType<Button>().Where(button => button.Text == text).ToArray();
                Assert.That(buttons, Has.Length.EqualTo(2), "One on the licence card, one beside the licence picker.");
                licencePreview = buttons[0];
                offerPreview = buttons[1];
                picker = Descendants(tabs).OfType<OptionButton>().Single(option => option.ItemCount == offers.Length);
            });
            await pair.RunTicksSync(5);

            await RoundSetupUiTest.Click(pair, licencePreview);
            await Showing(guppy.Name);

            await RoundSetupUiTest.Click(pair, offerPreview);
            await Showing(offers[0].Name);

            // A later offer: the picker swaps the open previewer without another press.
            var chosen = offers.Skip(1).First(offer => offer.Name != offers[0].Name);
            await RoundSetupUiTest.Click(pair, picker);
            Button option = null!;
            await pair.Client.WaitAssertion(() =>
            {
                var caption = Loc.GetString("wg-company-hangar-offer", ("name", chosen.Name),
                    ("price", Loc.GetString("wg-company-credits", ("amount", chosen.Price))));
                option = Descendants(picker.OptionsScroll).OfType<Button>().First(button => button.Text == caption);
            });
            await RoundSetupUiTest.Click(pair, option);
            await Showing(chosen.Name);

            await pair.Client.WaitAssertion(() =>
            {
                var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
                Assert.That(Descendants(ui.WindowRoot).OfType<ShipPreviewWindow>().Count(), Is.EqualTo(1), "Every press reuses one previewer.");
                window.Close();
                Assert.That(Descendants(ui.WindowRoot).OfType<ShipPreviewWindow>(), Is.Empty, "The previewer closes with the company window.");
                Assert.That(maps.GetAllMapIds(), Is.EquivalentTo(originalMaps), "Closing must release the preview map.");
            });
        }
        finally
        {
            if (id is { } left)
                await server.WaitPost(() => companies.AdminDisband(left));
            await Run(() => companies.Flush());
            await server.WaitPost(() => preferences.SetProfile(owner.UserId, slot, original).Wait());
            await Run(() => companies.Flush());
            await server.RemoveAllDummySessions();
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        async Task Showing(string name)
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            ShipPreviewControl preview = null!;
            await pair.Client.WaitAssertion(() =>
            {
                var shown = Descendants(ui.WindowRoot).OfType<ShipPreviewWindow>().Single();
                preview = Descendants(shown).OfType<ShipPreviewControl>().Single();
                Assert.That(Descendants(shown).OfType<Label>().Select(label => label.Text), Does.Contain(name));
            });
            await PoolManager.WaitUntil(pair.Client, () => preview.HasPreview, maxTicks: 180);
            await pair.Client.WaitAssertion(() => Assert.That(preview.TileCount, Is.Positive, name));
        }

        async Task<WGCompanyState> Send(WGCompanyRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await pair.Client.WaitPost(() => client.Send(request));
            await pair.RunTicksSync(pair.SecondsToTicks(0.75f));
            WGCompanyState result = null;
            await pair.Client.WaitPost(() => result = client.State);
            return result;
        }

        async Task Run(System.Func<Task> start)
        {
            Task task = null;
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => task = start());
            for (var i = 0; i < 600 && !task.IsCompleted; i++)
                await pair.RunTicksSync(1);
            await task;
        }

        static IEnumerable<Control> Descendants(Control control)
        {
            foreach (var child in control.Children)
            {
                yield return child;
                foreach (var nested in Descendants(child))
                    yield return nested;
            }
        }
    }
}
