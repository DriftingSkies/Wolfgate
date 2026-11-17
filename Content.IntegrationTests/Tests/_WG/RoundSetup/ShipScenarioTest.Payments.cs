using System.Linq;
using Content.Server._WG.RoundSetup;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server.Voting.Managers;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Robust.Shared.Enums;

namespace Content.IntegrationTests.Tests._WG.RoundSetup;

public sealed partial class ShipScenarioTest
{
    /// <summary>Split receipts reject invalid plans and unaffordable shares, charge once and refund every original slot once.</summary>
    [Test]
    public async Task SplitPurchaseValidatesChargesAndRefundsEveryContributor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var host = pair.Player!;
        var saved = prefs.GetPreferences(host.UserId);
        var slot = saved.SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) saved.Characters[slot];
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                vote.Cancel();
        });
        await pair.RunTicksSync(2);
        var member = (await server.AddDummySessions(1)).Single();
        try
        {
            var loaded = false;
            for (var i = 0; i < 300 && !loaded; i++)
            {
                await server.WaitPost(() => loaded = member.Status == SessionStatus.InGame && prefs.HavePreferencesLoaded(member) &&
                    server.ResolveDependency<UserDbDataManager>().IsLoadComplete(member));
                if (!loaded) { await pair.RunTicksSync(1); await Task.Delay(10); }
            }
            Assert.That(loaded, Is.True);
            var memberSlot = prefs.GetPreferences(member.UserId).SelectedCharacterIndex;
            await SetProfile(pair, slot, original.WithoutAllTraitPreferences().WithBankBalance(10000));
            await server.WaitPost(() => prefs.SetProfile(member.UserId, memberSlot,
                HumanoidCharacterProfile.DefaultWithSpecies("Human").WithName("Crew Medic").WithoutAllTraitPreferences().WithBankBalance(500)).Wait());
            await StartEmptyRound(pair);
            await server.WaitAssertion(() =>
            {
                var ships = server.System<WGShipScenarioSystem>();
                Assert.That(ships.TryPrepare(19180, Vessel, out _, out var reason), Is.True, reason);
                var shares = new[] { new WGShipScenarioSystem.Contribution(host, slot, 400), new WGShipScenarioSystem.Contribution(member, memberSlot, 600) };
                Assert.That(ships.TryCharge(19180, host, shares, out _), Is.False);
                Assert.That(ships.GetBalance(host), Is.EqualTo(10000));
                Assert.That(ships.GetBalance(member), Is.EqualTo(500));
                var memberProfile = (HumanoidCharacterProfile) prefs.GetPreferences(member.UserId).SelectedCharacter!;
                prefs.SetProfile(member.UserId, memberSlot, memberProfile.WithBankBalance(10000)).Wait();
                Assert.That(ships.TryCharge(19180, host, new[] { shares[0], shares[0] }, out _), Is.False);
                Assert.That(ships.TryCharge(19180, host, new[] { shares[0], shares[1] with { Slot = memberSlot + 1 } }, out _), Is.False);
                Assert.That(ships.TryCharge(19180, host, shares, out reason), Is.True, reason);
                Assert.That(ships.TryCharge(19180, host, shares, out reason), Is.True, reason);
                Assert.That(ships.GetBalance(host), Is.EqualTo(9600));
                Assert.That(ships.GetBalance(member), Is.EqualTo(9400));
                ships.Rollback(19180);
                ships.Rollback(19180);
                Assert.That(ships.GetBalance(host), Is.EqualTo(10000));
                Assert.That(ships.GetBalance(member), Is.EqualTo(10000));
            });
        }
        finally
        {
            await server.RemoveAllDummySessions();
            await SetProfile(pair, slot, original);
            await ReturnToLobby(pair);
            await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote));
        }
        await pair.CleanReturnAsync();
    }
}
