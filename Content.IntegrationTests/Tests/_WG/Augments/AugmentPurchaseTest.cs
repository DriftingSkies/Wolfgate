using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WG.InRoundCreator;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.IntegrationTests.Pair;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.Augments;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// A client save buys a priced augment once from the character's bank and keeps it unlocked; one the bank can't cover
/// is dropped, unlocks a client sends are ignored, and one taken off is sold back for half its price, or for nothing
/// if it was kept from a round. A client with a stale copy is sent the settled one, and the creator open over a round
/// shows it.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerPreferencesManager))]
public sealed class AugmentPurchaseTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wgAugment
  id: WGTestPricedLeftLeg
  name: wg-augment-cyber-leg
  description: wg-augment-cyber-leg-desc
  slot: LeftLeg
  kind: Limb
  part: WGAugmentLeftLeg
  styled: true
  price: 15000

- type: wgAugment
  id: WGTestPricedRightLeg
  name: wg-augment-cyber-leg
  description: wg-augment-cyber-leg-desc
  slot: RightLeg
  kind: Limb
  part: WGAugmentRightLeg
  styled: true
  price: 15000
";

    [Test]
    public async Task PurchaseTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var client = pair.Client;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var states = client.ResolveDependency<IStateManager>();

        await pair.RunTicksSync(1);
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState, 600);
        var userId = clientNet.ServerChannel!.UserId;
        const int slot = 0;

        HumanoidCharacterProfile Stored() => (HumanoidCharacterProfile) prefs.GetPreferences(userId).Characters[slot];

        // Starts a save on the server thread and ticks until its database write finishes.
        async Task Save(Func<Task> save)
        {
            Task task = default!;
            await server.WaitPost(() => task = save());
            await PoolManager.WaitUntil(server, () => task.IsCompleted, maxTicks: 300);
            Assert.That(task.IsCompletedSuccessfully, $"The save failed: {task.Exception}");
        }

        var start = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithBankBalance(20000);
        await Save(() => prefs.SetProfile(userId, slot, start));

        // Buys the left leg: 15000 of 20000.
        await Save(() => prefs.SetProfile(userId, slot, Stored().WithAugments(Legs(false)), authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().BankBalance, Is.EqualTo(5000));
            Assert.That(Stored().AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WGTestPricedLeftLeg" }));
            Assert.That(Stored().Augments, Contains.Key(WGAugmentSlot.LeftLeg));
        });

        // A client saving a copy from before the purchase isn't charged again, and is sent the settled one.
        var clientPrefs = client.ResolveDependency<IClientPreferencesManager>();
        var stale = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithBankBalance(20000).WithAugments(Legs(false));
        await client.WaitPost(() => clientPrefs.UpdateCharacter(stale, slot));
        HumanoidCharacterProfile copy = default!;
        await Until(pair, client, () =>
        {
            copy = (HumanoidCharacterProfile) clientPrefs.Preferences.Characters[slot];
            return copy.AugmentUnlocks.Count > 0;
        });
        Assert.Multiple(() =>
        {
            Assert.That(copy.AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WGTestPricedLeftLeg" }),
                "The client kept its stale copy, which shows the leg as not bought.");
            Assert.That(copy.BankBalance, Is.EqualTo(5000));
        });
        await server.WaitAssertion(() => Assert.That(Stored().BankBalance, Is.EqualTo(5000), "The same leg was charged twice."));

        // The right leg costs more than is left, so it is dropped; the left one is already paid for.
        await Save(() => prefs.SetProfile(userId, slot, Stored().WithAugments(Legs(true)), authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().BankBalance, Is.EqualTo(5000));
            Assert.That(Stored().Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.LeftLeg }));
        });

        // A client can't grant itself an unlock.
        var forged = Stored().WithAugmentUnlocks(new ProtoId<WGAugmentPrototype>[] { "WGTestPricedLeftLeg", "WGTestPricedRightLeg" })
            .WithAugments(Legs(true));
        await Save(() => prefs.SetProfile(userId, slot, forged, authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WGTestPricedLeftLeg" }));
            Assert.That(Stored().Augments.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.LeftLeg }));
        });

        // Taking the leg off sells it back for half: 5000 + 7500.
        await Save(() => prefs.SetProfile(userId, slot,
            Stored().WithAugments(new Dictionary<WGAugmentSlot, WGAugmentChoice>()), authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().BankBalance, Is.EqualTo(12500));
            Assert.That(Stored().AugmentUnlocks, Is.Empty);
            Assert.That(Stored().Augments, Is.Empty);
        });

        // A leg kept from a round was never paid for: taking it off pays nothing, whatever the client says of it.
        var left = new ProtoId<WGAugmentPrototype>[] { "WGTestPricedLeftLeg" };
        await Save(() => prefs.SetProfile(userId, slot,
            Stored().WithAugments(Legs(false)).WithAugmentUnlocks(left).WithAugmentFound(left)));
        await server.WaitAssertion(() => Assert.That(Stored().AugmentFound.Select(u => u.Id), Is.EquivalentTo(new[] { "WGTestPricedLeftLeg" })));
        await Save(() => prefs.SetProfile(userId, slot,
            Stored().WithAugmentFound(new ProtoId<WGAugmentPrototype>[0]).WithAugments(new Dictionary<WGAugmentSlot, WGAugmentChoice>()),
            authoritative: false));
        await server.WaitAssertion(() =>
        {
            Assert.That(Stored().BankBalance, Is.EqualTo(12500));
            Assert.That(Stored().AugmentUnlocks, Is.Empty);
            Assert.That(Stored().AugmentFound, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A purchase confirmed in the creator opened over a round shows there as owned, with the new balance.</summary>
    [Test]
    public async Task InRoundTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var clientPrefs = client.ResolveDependency<IClientPreferencesManager>();
        var states = client.ResolveDependency<IStateManager>();
        var ui = client.ResolveDependency<IUserInterfaceManager>();
        var userId = pair.Player!.UserId;

        await Until(pair, client, () => states.CurrentState is GameplayState);
        Assert.That(states.CurrentState, Is.TypeOf<GameplayState>());
        var slot = 0;
        await server.WaitPost(() => slot = prefs.GetPreferences(userId).SelectedCharacterIndex);

        HumanoidCharacterProfile Stored() => (HumanoidCharacterProfile) prefs.GetPreferences(userId).Characters[slot];

        Task save = default!;
        await server.WaitPost(() =>
            save = prefs.SetProfile(userId, slot, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithBankBalance(20000)));
        await PoolManager.WaitUntil(server, () => save.IsCompleted, maxTicks: 300);
        await server.WaitPost(() => prefs.ResendPreferences(userId));
        await Until(pair, client, () => clientPrefs.Preferences.Characters[slot] is HumanoidCharacterProfile { BankBalance: 20000 });

        var lobby = ui.GetUIController<LobbyUIController>();
        HumanoidProfileEditor editor = default!;
        await client.WaitAssertion(() =>
        {
            lobby.OpenInRoundCharacterSetup();
            editor = Descendants(ui.WindowRoot).OfType<HumanoidProfileEditor>().Single();
            Assert.That(editor.Profile?.BankBalance, Is.EqualTo(20000));

            // What the Confirm button does: save, then show the client's own copy until the server answers.
            clientPrefs.UpdateCharacter(editor.Profile!.WithAugments(Legs(false)), slot);
            lobby.ReloadCharacterSetup();
            Assert.That(editor.Profile!.AugmentUnlocks, Is.Empty);
        });

        await Until(pair, client, () => editor.Profile is { AugmentUnlocks.Count: > 0 });
        await client.WaitAssertion(() =>
        {
            Assert.That(editor.Profile!.AugmentUnlocks.Select(u => u.Id), Is.EquivalentTo(new[] { "WGTestPricedLeftLeg" }),
                "The creator still shows the leg as not bought.");
            Assert.That(editor.Profile!.BankBalance, Is.EqualTo(5000), "The creator still shows the balance from before the purchase.");
            Assert.That(editor.IsDirty, Is.False);
            Descendants(ui.WindowRoot).OfType<InRoundCreatorWindow>().Single().ForceClose();
        });
        await server.WaitAssertion(() => Assert.That(Stored().BankBalance, Is.EqualTo(5000)));

        await pair.CleanReturnAsync();
    }

    private static Dictionary<WGAugmentSlot, WGAugmentChoice> Legs(bool both)
    {
        var legs = new Dictionary<WGAugmentSlot, WGAugmentChoice>
        {
            [WGAugmentSlot.LeftLeg] = new("WGTestPricedLeftLeg", "WGAugmentStyleCyborg"),
        };
        if (both)
            legs[WGAugmentSlot.RightLeg] = new("WGTestPricedRightLeg", "WGAugmentStyleCyborg");
        return legs;
    }

    /// <summary>Ticks both sides until a check on the client passes, or gives up; the caller asserts what it waited for.</summary>
    private static async Task Until(TestPair pair, RobustIntegrationTest.ClientIntegrationInstance client, Func<bool> check)
    {
        var passed = false;
        for (var i = 0; i < 120 && !passed; i++)
        {
            await pair.RunTicksSync(5);
            await client.WaitPost(() => passed = check());
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
