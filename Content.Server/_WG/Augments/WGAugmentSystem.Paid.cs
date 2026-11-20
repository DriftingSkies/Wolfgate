using System.Linq;
using System.Threading.Tasks;
using Content.Server.Cargo.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WG.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WG.Augments;

/// <summary>
/// Follows paid-for hardware through a round. A part bought in the creator is marked when its character spawns, and
/// the mark is what it is worth. Cut out of its body, the part is priced at what its augments sell back for, so it
/// sells at a depot like other loot; whoever ends a round with it in their body instead owns the augment as paid
/// for. Either way the character it was taken from loses it. A purchase has one mark and a copy of the part has
/// none, so nothing sells twice.
/// </summary>
public sealed partial class WGAugmentSystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private GameTicker _ticker = default!;

    /// <summary>Each player's last queued profile write.</summary>
    private readonly Dictionary<NetUserId, Task> _profileWrites = new();

    private void InitializePaid()
    {
        SubscribeLocalEvent<WGAugmentPaidComponent, PriceCalculationEvent>(OnPaidPrice);
        SubscribeLocalEvent<WGAugmentPaidComponent, EntityTerminatingEvent>(OnPaidTerminating);
    }

    /// <summary>A marked part outside the body it was counted in is worth what its augments sell back for.</summary>
    private void OnPaidPrice(Entity<WGAugmentPaidComponent> ent, ref PriceCalculationEvent args)
    {
        var body = BodyOf(ent);
        foreach (var (id, mark) in ent.Comp.Augments)
        {
            if (mark.Body != body && _proto.TryIndex(id, out var augment))
                args.Price += WGAugmentRules.Refund(augment);
        }
    }

    private void OnPaidTerminating(Entity<WGAugmentPaidComponent> ent, ref EntityTerminatingEvent args)
    {
        // Round cleanup deletes everything; only a part sold or destroyed during the round is lost.
        if (_ticker.RunLevel == GameRunLevel.InRound)
            LoseDestroyed(ent);
    }

    /// <summary>
    /// A marked part sold or destroyed outside its owner's body is lost to that character. One that goes with the
    /// body it is in, or with a body being deleted in the same breath, is not.
    /// </summary>
    internal Task LoseDestroyed(Entity<WGAugmentPaidComponent> ent)
    {
        var body = BodyOf(ent);
        var lost = ent.Comp.Augments
            .Where(a => a.Value.Owner != null && a.Value.Body != body && !BeingDeleted(a.Value.Body))
            .GroupBy(a => a.Value.Owner!.Value, a => a.Key);
        return Task.WhenAll(lost.Select(owned => Revoke(owned.Key, owned.ToHashSet())).ToList());
    }

    private bool BeingDeleted(EntityUid uid)
    {
        return !Deleted(uid) && (LifeStage(uid) >= EntityLifeStage.Terminating || EntityManager.IsQueuedForDeletion(uid));
    }

    /// <summary>The part or organ of a body that carries an augment.</summary>
    private EntityUid? Holder(EntityUid uid, WGAugmentPrototype augment)
    {
        if (!TryComp<BodyComponent>(uid, out var comp))
            return null;

        Entity<BodyComponent> body = (uid, comp);
        return augment.Kind switch
        {
            WGAugmentKind.Limb => FindLimb(body, augment.Slot)?.Id,
            WGAugmentKind.Organ => OrganIn(body, augment.Slot),
            WGAugmentKind.Upgrade when Surgeon(augment) is { } surgeon => _autoSurgeon.WgUpgradeTarget(uid, surgeon),
            _ => null,
        };
    }

    /// <summary>The body a part or organ is in.</summary>
    private EntityUid? BodyOf(EntityUid uid)
    {
        return CompOrNull<BodyPartComponent>(uid)?.Body ?? CompOrNull<OrganComponent>(uid)?.Body;
    }

    /// <summary>Whether a part other than <paramref name="except"/> carries the character's mark for an augment.</summary>
    private bool MarkedElsewhere(WGAugmentOwner owner, ProtoId<WGAugmentPrototype> id, EntityUid? except)
    {
        var query = AllEntityQuery<WGAugmentPaidComponent>();
        while (query.MoveNext(out var uid, out var paid))
        {
            if (uid != except && paid.Augments.TryGetValue(id, out var mark) && mark.Owner == owner)
                return true;
        }

        return false;
    }

    /// <summary>Marks the paid-for augments a body spawned with as its character's, in place of last life's marks.</summary>
    internal void MarkPaid(EntityUid body, HumanoidCharacterProfile profile, WGAugmentOwner owner)
    {
        foreach (var id in ReadInstalled(body))
        {
            if (!profile.AugmentUnlocks.Contains(id)
                || profile.AugmentFound.Contains(id)
                || Holder(body, _proto.Index(id)) is not { } holder)
                continue;

            var query = AllEntityQuery<WGAugmentPaidComponent>();
            while (query.MoveNext(out var paid))
            {
                if (paid.Augments.TryGetValue(id, out var old) && old.Owner == owner)
                    paid.Augments.Remove(id);
            }

            EnsureComp<WGAugmentPaidComponent>(holder).Augments[id] = new WGAugmentMark(owner, body);
        }
    }

    /// <summary>
    /// A paid-for augment sold in the creator is sold with its hardware, which is worth nothing after. Hardware that
    /// has left the character's body can't be sold: the mark stays on it for whoever has it.
    /// </summary>
    /// <returns>Whether the sale pays.</returns>
    public bool SellPaid(NetUserId user, int slot, ProtoId<WGAugmentPrototype> id)
    {
        var owner = new WGAugmentOwner(user, slot);
        var pays = true;
        var query = AllEntityQuery<WGAugmentPaidComponent>();
        while (query.MoveNext(out var uid, out var paid))
        {
            if (!paid.Augments.TryGetValue(id, out var mark) || mark.Owner != owner)
                continue;

            if (BodyOf(uid) == mark.Body)
            {
                paid.Augments.Remove(id);
                continue;
            }

            paid.Augments[id] = mark with { Owner = null };
            pays = false;
        }

        return pays;
    }

    /// <summary>
    /// Whether an augment the character keeps was paid for. A marked part in its body is, and becomes the
    /// character's; another character's mark is noted in <paramref name="taken"/>. With its own mark on a part
    /// elsewhere, what it has is a copy. Else its profile says, and a part that came without a mark gets one.
    /// </summary>
    private bool ClaimPaid(EntityUid body,
        ProtoId<WGAugmentPrototype> id,
        bool carried,
        WGAugmentOwner owner,
        HumanoidCharacterProfile profile,
        Dictionary<WGAugmentOwner, HashSet<ProtoId<WGAugmentPrototype>>> taken)
    {
        var holder = carried && _proto.TryIndex(id, out var augment) ? Holder(body, augment) : null;
        if (TryComp<WGAugmentPaidComponent>(holder, out var paid) && paid.Augments.TryGetValue(id, out var mark))
        {
            if (mark.Owner is { } victim && victim != owner)
                taken.GetOrNew(victim).Add(id);
            paid.Augments[id] = new WGAugmentMark(owner, body);
            return true;
        }

        if (MarkedElsewhere(owner, id, holder)
            || !profile.AugmentUnlocks.Contains(id)
            || profile.AugmentFound.Contains(id))
            return false;

        if (holder != null)
            EnsureComp<WGAugmentPaidComponent>(holder.Value).Augments[id] = new WGAugmentMark(owner, body);
        return true;
    }

    /// <summary>
    /// Gives up the character's marks on parts its body no longer carries, so whoever keeps one owns it without
    /// taking anything more from this character.
    /// </summary>
    private void ReleasePaid(EntityUid? body, WGAugmentOwner owner)
    {
        var query = AllEntityQuery<WGAugmentPaidComponent>();
        while (query.MoveNext(out var uid, out var paid))
        {
            foreach (var (id, mark) in paid.Augments.Where(a => a.Value.Owner == owner).ToList())
            {
                if (body == null || !_proto.TryIndex(id, out var augment) || Holder(body.Value, augment) != uid)
                    paid.Augments[id] = mark with { Owner = null };
            }
        }
    }

    /// <summary>
    /// Takes augments off the profile of the character they were taken from, online or not, once someone else has
    /// kept or sold the hardware.
    /// </summary>
    private Task Revoke(WGAugmentOwner victim, HashSet<ProtoId<WGAugmentPrototype>> ids)
    {
        // The victim's body is read like any other: these slots no longer count as changed in the creator.
        if (_played.TryGetValue(victim.User, out var played) && played.Slot == victim.Slot)
        {
            foreach (var slot in played.Spawned.Where(s => ids.Contains(s.Value.Augment)).Select(s => s.Key).ToList())
                played.Spawned.Remove(slot);
            played.SpawnedImplants.ExceptWith(ids);
        }

        return Queued(victim.User, () => RevokeProfile(victim, ids));
    }

    private async Task RevokeProfile(WGAugmentOwner victim, HashSet<ProtoId<WGAugmentPrototype>> ids)
    {
        try
        {
            if (_players.TryGetSessionById(victim.User, out var session)
                && _preferences.TryGetCachedPreferences(victim.User, out var cached))
            {
                if (cached.Characters.GetValueOrDefault(victim.Slot) is not HumanoidCharacterProfile profile
                    || Without(profile, ids) is not { } left)
                    return;

                var names = string.Join(", ", ids.Where(profile.AugmentUnlocks.Contains)
                    .Select(id => WGAugmentRules.Name(_proto.Index(id), _proto)));
                _chat.DispatchServerMessage(session, Loc.GetString("wg-augments-stolen", ("name", profile.Name), ("augments", names)));
                Log.Info($"{profile.Name} of user {victim.User} lost stolen augments: {string.Join(", ", ids)}.");

                var selected = cached.SelectedCharacterIndex;
                await _preferences.SetProfile(victim.User, victim.Slot, left);

                // An authoritative save selects its slot, and the bank writes to the selected one.
                if (selected != victim.Slot
                    && _preferences.GetPreferencesOrNull(victim.User)?.Characters.GetValueOrDefault(selected) is { } current)
                    await _preferences.SetProfile(victim.User, selected, current);
                _preferences.ResendPreferences(victim.User);
                return;
            }

            var stored = await _db.GetPlayerPreferencesAsync(victim.User, default);
            if (stored?.Characters.GetValueOrDefault(victim.Slot) is HumanoidCharacterProfile offline
                && Without(offline, ids) is { } rest)
            {
                await _db.SaveCharacterSlotAsync(victim.User, rest, victim.Slot);
                Log.Info($"{offline.Name} of offline user {victim.User} lost stolen augments: {string.Join(", ", ids)}.");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to take stolen augments off {victim.User}'s character {victim.Slot}: {e}");
        }
    }

    /// <summary>
    /// Runs a write to a player's profiles after the ones queued before it, so two that read the stored profile
    /// first don't save over each other's change.
    /// </summary>
    private Task Queued(NetUserId user, Func<Task> write)
    {
        var task = After(_profileWrites.GetValueOrDefault(user) ?? Task.CompletedTask, write);
        _profileWrites[user] = task;
        return task;
    }

    private static async Task After(Task previous, Func<Task> write)
    {
        // Every queued write logs its own failure.
        await previous.ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
        await write();
    }

    /// <summary>The profile without the augments, their picks and their unlocks; null if it owns none of them.</summary>
    private static HumanoidCharacterProfile? Without(HumanoidCharacterProfile profile, HashSet<ProtoId<WGAugmentPrototype>> ids)
    {
        if (!profile.AugmentUnlocks.Overlaps(ids))
            return null;

        return profile.WithAugments(profile.Augments.Where(a => !ids.Contains(a.Value.Augment)).ToDictionary(a => a.Key, a => a.Value))
            .WithAugmentImplants(profile.AugmentImplants.Where(i => !ids.Contains(i)).ToHashSet())
            .WithAugmentUnlocks(profile.AugmentUnlocks.Where(u => !ids.Contains(u)));
    }
}
