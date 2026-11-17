using System.Linq;
using System.Threading.Tasks;
using Content.Client._WG.Companies;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Companies;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.Companies;
using Content.Shared.Preferences;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Companies;

/// <summary>Companies: creation, membership, ranks and permissions, money, persistence and deleted characters.</summary>
[TestFixture]
[TestOf(typeof(WGCompanySystem))]
public sealed partial class WGCompanyTest
{
    [Test]
    public async Task CompaniesPersistWithRanksAndMoney()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var companies = server.System<WGCompanySystem>();
        var client = pair.Client.System<WGCompanyClientSystem>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var owner = pair.Player!;
        var original = (HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).SelectedCharacter;
        var slot = preferences.GetPreferences(owner.UserId).SelectedCharacterIndex;
        var players = await server.AddDummySessions(2);
        var officer = players[0];
        var ironman = players[1];
        int? id = null;
        try
        {
            await WaitFor(() => companies.Loaded && players.All(player => player.Status == SessionStatus.InGame &&
                preferences.HavePreferencesLoaded(player)));
            await server.WaitPost(() =>
            {
                preferences.SetProfile(owner.UserId, slot, original.WithName("Ada Owner").WithBankBalance(10000)).Wait();
                var officerPrefs = preferences.GetPreferences(officer.UserId);
                preferences.SetProfile(officer.UserId, officerPrefs.SelectedCharacterIndex,
                    ((HumanoidCharacterProfile) officerPrefs.SelectedCharacter).WithName("Bo Officer").WithBankBalance(0)).Wait();
                var ironPrefs = preferences.GetPreferences(ironman.UserId);
                preferences.SetProfile(ironman.UserId, ironPrefs.SelectedCharacterIndex, ((HumanoidCharacterProfile) ironPrefs.SelectedCharacter)
                    .WithName("Cy Iron").WithTraitPreference("Ironman", server.ResolveDependency<IPrototypeManager>())).Wait();
            });

            await RenderWindow();
            var state = await Request(new(WGCompanyAction.Create) { Name = "Hawkline Salvage", Tag = "hawk", Description = "Scrap and salvage.",
                Colour = Color.FromHex("#3366CC") });
            Assert.That(state.Own, Is.Not.Null, state.Message);
            var own = state.Own!;
            id = own.Id;
            Assert.Multiple(() =>
            {
                Assert.That(own.Tag, Is.EqualTo("HAWK"));
                Assert.That(own.Ranks.Select(rank => rank.Title), Is.EqualTo(new[] { "Officer", "Member", "Recruit" }));
                Assert.That(own.YourPermissions, Is.EqualTo(WGCompanyPermissions.All));
                Assert.That(own.Cut, Is.EqualTo(WGCompanyPolicy.DefaultCut));
            });

            await RequestAs(ironman, new(WGCompanyAction.Create) { Name = "Iron Will", Tag = "IRON" });
            await RequestAs(officer, new(WGCompanyAction.Create) { Name = "hawkline salvage", Tag = "OTHER" });
            await RequestAs(officer, new(WGCompanyAction.Create) { Name = "Different Name", Tag = "hawk" });
            await server.WaitAssertion(() => Assert.That(companies.ListCompanies().Count(), Is.EqualTo(1),
                "Ironman characters, duplicate names and duplicate tags (in any case) can't create companies."));

            state = await Request(new(WGCompanyAction.Refresh));
            Assert.That(state.InviteTargets.Select(target => target.Name), Does.Contain("Bo Officer"));
            Assert.That(state.InviteTargets.Select(target => target.Name), Does.Not.Contain("Cy Iron"));
            await Request(new(WGCompanyAction.Invite) { CompanyId = own.Id, TargetUser = officer.UserId });
            await RequestAs(officer, new(WGCompanyAction.AcceptInvite) { CompanyId = own.Id });
            state = await Request(new(WGCompanyAction.Refresh));
            var bo = state.Own!.Members.Single(member => member.Name == "Bo Officer");
            Assert.That(bo.Rank, Is.EqualTo(state.Own.Ranks[^1].Id), "New members start at the lowest rank.");

            var logo = new byte[WGCompanyPolicy.LogoBytes];
            logo[3] = 255;
            state = await Request(new(WGCompanyAction.SetLogo) { CompanyId = own.Id, Logo = logo });
            Assert.That(state.Own!.LogoVersion, Is.GreaterThan(0), state.Message);
            await RequestAs(officer, new(WGCompanyAction.SetLogo) { CompanyId = own.Id, Logo = new byte[WGCompanyPolicy.LogoBytes] });
            state = await Request(new(WGCompanyAction.Refresh));
            var logoVersion = state.Own!.LogoVersion;
            await pair.Client.WaitPost(() => client.GetLogo(own.Id, logoVersion));
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(client.GetLogoPixels(own.Id, logoVersion)?[3], Is.EqualTo(255), "Recruits can't change the logo; clients fetch it.");
                Assert.That(client.GetLogo(own.Id, logoVersion), Is.Not.Null);
            });
            WGCompanyLogoWindow editor = null;
            await pair.Client.WaitPost(() =>
            {
                editor = new WGCompanyLogoWindow("Logo", WGCompanyPolicy.LogoSize, client.GetLogoPixels(own.Id, logoVersion), _ => { });
                editor.OpenCentered();
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.3f));
            await pair.Client.WaitPost(() => editor.Close());
            await pair.Client.WaitPost(() =>
            {
                editor = new WGCompanyLogoWindow("Icon", WGCompanyPolicy.IconSize, null, _ => { });
                editor.OpenCentered();
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.3f));
            await pair.Client.WaitPost(() => editor.Close());
            await RenderWindow();
            state = await Request(new(WGCompanyAction.Deposit) { CompanyId = own.Id, Amount = 1000 });
            Assert.That(state.Own!.Balance, Is.EqualTo(1000), state.Message);
            Assert.That(state.CharacterBalance, Is.EqualTo(9000));
            Assert.That(state.Own.Ledger.Single().Kind, Is.EqualTo(WGCompanyLedgerKind.Deposit));

            await RequestAs(officer, new(WGCompanyAction.Withdraw) { CompanyId = own.Id, Amount = 100 });
            await server.WaitAssertion(() => Assert.That(companies.GetBalance(own.Id), Is.EqualTo(1000), "Recruits can't withdraw."));

            var officerRank = state.Own.Ranks[0];
            await Request(new(WGCompanyAction.UpsertRank)
            {
                CompanyId = own.Id, RankId = officerRank.Id, Name = "Quartermaster", Wage = 50, WithdrawCap = 300, BaseJob = "Pilot",
                Permissions = officerRank.Permissions | WGCompanyPermissions.Withdraw,
            });
            state = await Request(new(WGCompanyAction.SetMemberRank) { CompanyId = own.Id, Target = bo.Profile, RankId = officerRank.Id });
            Assert.That(state.Own!.Members.Single(member => member.Profile == bo.Profile).Rank, Is.EqualTo(officerRank.Id), state.Message);

            await RequestAs(officer, new(WGCompanyAction.Withdraw) { CompanyId = own.Id, Amount = 500 });
            await server.WaitAssertion(() => Assert.That(companies.GetBalance(own.Id), Is.EqualTo(1000), "Withdrawals respect the daily cap."));
            await RequestAs(officer, new(WGCompanyAction.Withdraw) { CompanyId = own.Id, Amount = 200 });
            await server.WaitAssertion(() =>
            {
                Assert.That(companies.GetBalance(own.Id), Is.EqualTo(800));
                Assert.That(((HumanoidCharacterProfile) preferences.GetPreferences(officer.UserId).SelectedCharacter).BankBalance, Is.EqualTo(200));
            });

            var recruit = state.Own.Ranks[^1];
            await RequestAs(officer, new(WGCompanyAction.UpsertRank)
            {
                CompanyId = own.Id, RankId = recruit.Id, Name = recruit.Title, BaseJob = recruit.BaseJob, Permissions = WGCompanyPermissions.Design,
            });
            await RequestAs(officer, new(WGCompanyAction.Kick) { CompanyId = own.Id, Target = state.Own.YourProfile });
            state = await Request(new(WGCompanyAction.Refresh));
            Assert.Multiple(() =>
            {
                Assert.That(state.Own!.Ranks[^1].Permissions, Is.EqualTo(WGCompanyPermissions.None), "Nobody grants a permission they lack.");
                Assert.That(state.Own.Members, Has.Length.EqualTo(2), "Officers can't kick the owner.");
            });

            await server.WaitPost(() => companies.SetFrozen(own.Id, true));
            state = await Request(new(WGCompanyAction.Deposit) { CompanyId = own.Id, Amount = 100 });
            Assert.That(state.Own!.Balance, Is.EqualTo(800), "A frozen account takes no deposits.");
            await server.WaitPost(() =>
            {
                Assert.That(companies.AdminAdjust(own.Id, -5000, "Test", "overdraw"), Is.False);
                Assert.That(companies.AdminAdjust(own.Id, 200, "Test", "correction"), Is.True);
                companies.SetFrozen(own.Id, false);
            });

            state = await Request(new(WGCompanyAction.UpsertRank) { CompanyId = own.Id, RankId = state.Own!.OwnerRank!.Id, Name = "Boss", BaseJob = "Pilot" });
            await Run(() => companies.Reload());
            state = await Request(new(WGCompanyAction.Refresh));
            Assert.Multiple(() =>
            {
                Assert.That(state.Own?.OwnerRank?.Title, Is.EqualTo("Boss"), "The owner rank loads back too.");
                Assert.That(state.Own?.OwnerRank?.BaseJob, Is.EqualTo("Pilot"));
                Assert.That(state.Own?.Name, Is.EqualTo("Hawkline Salvage"), "Companies load back from the database.");
                Assert.That(state.Own!.Balance, Is.EqualTo(1000));
                Assert.That(state.Own.Ranks[0].Title, Is.EqualTo("Quartermaster"));
                Assert.That(state.Own.Ranks[0].WithdrawCap, Is.EqualTo(300));
                Assert.That(state.Own.Ranks[0].BaseJob, Is.EqualTo("Pilot"));
                Assert.That(state.Own.Members.Single(member => member.Profile == bo.Profile).Rank, Is.EqualTo(officerRank.Id));
                Assert.That(state.Own.Ledger.Select(entry => entry.Kind),
                    Is.EqualTo(new[] { WGCompanyLedgerKind.AdminAdjust, WGCompanyLedgerKind.Withdraw, WGCompanyLedgerKind.Deposit }));
                Assert.That(state.Characters.Single().Rank, Is.EqualTo("Boss"));
                Assert.That(state.Directory.Single().Value, Is.EqualTo(1000), "A company with no licences is worth its account.");
                Assert.That(state.Directory.Single().Members, Is.EqualTo(2));
            });

            // A deleted owner hands the company to the remaining member.
            await Request(new(WGCompanyAction.Transfer) { CompanyId = own.Id, Target = bo.Profile });
            await Run(() => companies.Flush());
            var officerSlot = preferences.GetPreferences(officer.UserId).SelectedCharacterIndex;
            await Run(() => db.SaveCharacterSlotAsync(officer.UserId, null, officerSlot));
            await Run(() => companies.Reload());
            state = await Request(new(WGCompanyAction.Refresh));
            Assert.Multiple(() =>
            {
                Assert.That(state.Own!.OwnerProfile, Is.EqualTo(state.Own.YourProfile));
                Assert.That(state.Own.Members, Has.Length.EqualTo(1));
            });

            await Request(new(WGCompanyAction.Disband) { CompanyId = own.Id });
            await Run(() => companies.Reload());
            await server.WaitAssertion(() => Assert.That(companies.ListCompanies(), Is.Empty));
            id = null;
        }
        finally
        {
            if (id is { } left)
                await server.WaitPost(() => companies.AdminDisband(left));
            await Run(() => companies.Flush());
            await server.WaitPost(() => preferences.SetProfile(owner.UserId, slot, original).Wait());
            await server.RemoveAllDummySessions();
        }
        await pair.CleanReturnAsync();

        async Task<WGCompanyState> Request(WGCompanyRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await pair.Client.WaitPost(() => client.Send(request));
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
            await Run(() => companies.Flush());
            await pair.RunTicksSync(5);
            WGCompanyState result = null;
            await pair.Client.WaitPost(() => result = client.State);
            Assert.That(result, Is.Not.Null);
            return result;
        }

        // Builds every tab of the window for the current snapshot; any exception fails the test.
        async Task RenderWindow()
        {
            WGCompanyWindow window = null;
            await pair.Client.WaitPost(() =>
            {
                window = new WGCompanyWindow();
                window.OpenCentered();
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.3f));
            await pair.Client.WaitPost(() => window.Close());
        }

        async Task RequestAs(ICommonSession player, WGCompanyRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await Run(() => companies.HandleRequest(request, player));
            await Run(() => companies.Flush());
        }

        // Server tasks continue on the server thread, so they only finish while ticks run.
        async Task Run(Func<Task> start)
        {
            Task task = null;
            await server.WaitPost(() => task = start());
            for (var i = 0; i < 600 && !task.IsCompleted; i++)
            {
                await pair.RunTicksSync(1);
                await Task.Delay(5);
            }
            Assert.That(task.IsCompleted, Is.True, "Server task timed out.");
            await task;
        }

        async Task WaitFor(Func<bool> condition)
        {
            var done = false;
            for (var i = 0; i < 600 && !done; i++)
            {
                await server.WaitPost(() => done = condition());
                if (!done)
                {
                    await pair.RunTicksSync(1);
                    await Task.Delay(10);
                }
            }
            Assert.That(done, Is.True);
        }
    }
}
