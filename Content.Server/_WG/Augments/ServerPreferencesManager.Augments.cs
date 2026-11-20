using System.Linq;
using Content.Server._WG.Augments;
using Content.Shared._WG.Augments;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.Preferences.Managers;

public sealed partial class ServerPreferencesManager
{
    [Dependency] private IComponentFactory _wgFactory = default!;

    /// <summary>
    /// Settles the augments of a profile a client saved. Unlocks only come from the stored profile. An unlocked augment
    /// it no longer picks is given up first, sold back for <see cref="WGAugmentRules.RefundPercent"/> of its price if it
    /// was paid for and its hardware hasn't been taken, else for nothing; then a priced augment it hasn't unlocked is
    /// bought from the character's bank if the balance covers it, and dropped otherwise.
    /// </summary>
    /// <returns>
    /// Whether the client's copy is out of date: something was bought, sold or dropped, or the unlocks or the
    /// balance it sent aren't the settled ones.
    /// </returns>
    private bool SettleAugments(NetUserId userId,
        int character,
        ref HumanoidCharacterProfile profile,
        HumanoidCharacterProfile? stored,
        int? sentBalance)
    {
        var sent = profile;
        var hardware = _entityManager.System<WGAugmentSystem>();
        var owned = new HashSet<ProtoId<WGAugmentPrototype>>(stored?.AugmentUnlocks ?? new());
        var found = new HashSet<ProtoId<WGAugmentPrototype>>(stored?.AugmentFound ?? new());
        var unlocks = new HashSet<ProtoId<WGAugmentPrototype>>();
        var balance = profile.BankBalance;
        var name = profile.Name;
        var changed = false;

        // Sells an owned augment back; one never paid for, or whose hardware someone else has, pays nothing.
        void Sell(ProtoId<WGAugmentPrototype> id)
        {
            if (!_protos.TryIndex(id, out var sold))
                return;

            var refund = !found.Contains(id) && hardware.SellPaid(userId, character, id) ? WGAugmentRules.Refund(sold) : 0;
            balance += refund;
            _sawmill.Info($"User {userId} sold augment {id} off {name} for {refund}.");
        }

        var picked = WGAugmentRules.Picked(profile).ToHashSet();
        foreach (var id in owned.Where(o => !picked.Contains(o)).OrderBy(o => o.Id))
        {
            changed = true;
            Sell(id);
        }

        // Buys a priced augment the character doesn't own yet; false if the bank can't cover it.
        bool Buy(WGAugmentPrototype augment)
        {
            if (augment.Price <= 0)
                return true;
            if (owned.Contains(augment.ID))
            {
                unlocks.Add(augment.ID);
                return true;
            }

            changed = true;
            if (balance < augment.Price)
                return false;

            balance -= augment.Price;
            unlocks.Add(augment.ID);
            _sawmill.Info($"User {userId} unlocked augment {augment.ID} on {name} for {augment.Price}.");
            return true;
        }

        var choices = new Dictionary<WGAugmentSlot, WGAugmentChoice>();
        foreach (var (slot, choice) in profile.Augments.OrderBy(c => c.Key))
        {
            if (!_protos.TryIndex(choice.Augment, out var augment))
                changed = true;
            else if (Buy(augment))
                choices[slot] = choice;
        }

        // A limb dropped for want of money takes the implants it had room for.
        var fitting = WGAugmentRules.ValidateImplants(profile.Species, choices, profile.AugmentImplants,
            profile.TraitPreferences, _protos, _wgFactory, profile.Biotype);
        var implants = new HashSet<ProtoId<WGAugmentPrototype>>();
        foreach (var id in profile.AugmentImplants.OrderBy(i => i.Id))
        {
            if (fitting.Contains(id) && _protos.TryIndex(id, out var implant) && Buy(implant))
                implants.Add(id);
            else
                changed = true;
        }

        // An owned implant that lost its part with that limb is sold too.
        foreach (var id in owned.Where(o => picked.Contains(o) && !unlocks.Contains(o)).OrderBy(o => o.Id))
        {
            Sell(id);
        }

        profile = profile.WithBankBalance(balance)
            .WithAugmentUnlocks(unlocks)
            .WithAugmentFound(found)
            .WithAugments(choices)
            .WithAugmentImplants(implants);
        return changed
               || sentBalance != balance
               || !sent.AugmentUnlocks.SetEquals(profile.AugmentUnlocks)
               || !sent.AugmentFound.SetEquals(profile.AugmentFound);
    }

    /// <summary>Sends a player their preferences again, so the creator shows what the server changed.</summary>
    public void ResendPreferences(NetUserId userId)
    {
        if (_cachedPlayerPrefs.TryGetValue(userId, out var data) && data.Prefs is { } prefs)
            ResendPreferences(userId, prefs);
    }

    /// <summary>Sends a player their preferences again, so the creator shows what the server changed.</summary>
    private void ResendPreferences(NetUserId userId, PlayerPreferences prefs)
    {
        if (!_playerManager.TryGetSessionById(userId, out var session))
            return;

        _netManager.ServerSendMessage(new MsgPreferencesAndSettings
        {
            Preferences = prefs,
            Settings = new GameSettings { MaxCharacterSlots = MaxCharacterSlots },
        }, session.Channel);
    }
}
