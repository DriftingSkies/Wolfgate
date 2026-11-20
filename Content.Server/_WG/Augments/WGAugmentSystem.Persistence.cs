using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.CryoSleep;
using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Server.Preferences.Managers;
using Content.Server.Traits;
using Content.Shared._Shitmed.BodyEffects.Subsystems;
using Content.Shared._WG.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Augments;

/// <summary>
/// Keeps a character's augments in step with its body. When it goes into cryo or the round ends with it alive in the
/// body it spawned in, the paid-for cyberware still in that body is written to its profile, so what was lost is gone
/// and what the round put in without being paid for ends with the round. A character that respawns, is dead at the
/// end or ends up in another body loses all of it; one asleep in cryo is out of play until it wakes, so nothing
/// after takes them. A pick that never reached the body was never there to lose, so it and its unlock stay. Which
/// of it sells back in the creator is in <c>WGAugmentSystem.Paid.cs</c>.
/// </summary>
public sealed partial class WGAugmentSystem
{
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private CryoSleepSystem _cryoSleep = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>The character a player spawned as this round: its slot, its mind, its body and the augments it spawned with.</summary>
    internal sealed record PlayedCharacter(int Slot,
        EntityUid Mind,
        EntityUid Body,
        Dictionary<WGAugmentSlot, WGAugmentChoice> Spawned,
        HashSet<ProtoId<WGAugmentPrototype>> SpawnedImplants)
    {
        /// <summary>The picks that never reached the body: it had no part or organ to take them.</summary>
        public HashSet<ProtoId<WGAugmentPrototype>> Unfitted { get; init; } = new();
    }

    private readonly Dictionary<NetUserId, PlayedCharacter> _played = new();

    /// <summary>Characters asleep in cryo: saved going in, and back in play only if they wake.</summary>
    private readonly Dictionary<NetUserId, PlayedCharacter> _asleep = new();

    private void InitializePersistence()
    {
        // After traits, so augments under a trait's limbs are put back before the paid marks go on.
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete, after: new[] { typeof(TraitSystem) });
        SubscribeLocalEvent<BodyComponent, CryosleepBeforeMindRemovedEvent>(OnCryosleep);
        SubscribeLocalEvent<BodyComponent, CryosleepWakeUpEvent>(OnCryoWakeUp);
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _played.Clear();
            _asleep.Clear();
        });
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // A job that spawns its own entity (a borg, the station AI) never wears the character's body or its augments.
        if (!HasComp<HumanoidAppearanceComponent>(args.Mob))
            return;

        ReapplyOverTraits(args.Mob, args.Profile);
        var unfitted = Unfitted(args.Mob, args.Profile);
        if (unfitted.Count > 0)
            Log.Warning($"User {args.Player.UserId}'s {args.Profile.Name} spawned without picked augments: {string.Join(", ", unfitted)}.");

        // Selection is locked once a player joins, so the selected slot is the one that spawned.
        if (_preferences.GetPreferencesOrNull(args.Player.UserId) is { SelectedCharacter: HumanoidCharacterProfile selected } prefs
            && selected.Name == args.Profile.Name
            && _mind.TryGetMind(args.Mob, out var mind, out _))
        {
            Track(args.Player.UserId, new PlayedCharacter(prefs.SelectedCharacterIndex,
                mind,
                args.Mob,
                new(args.Profile.Augments),
                new(args.Profile.AugmentImplants)) { Unfitted = unfitted });
            MarkPaid(args.Mob, selected, new WGAugmentOwner(args.Player.UserId, prefs.SelectedCharacterIndex));
        }
    }

    internal void Track(NetUserId user, PlayedCharacter character)
    {
        _played[user] = character;
        _asleep.Remove(user);
    }

    /// <summary>The profile's picks a freshly spawned body doesn't carry.</summary>
    internal HashSet<ProtoId<WGAugmentPrototype>> Unfitted(EntityUid uid, HumanoidCharacterProfile profile)
    {
        var unfitted = new HashSet<ProtoId<WGAugmentPrototype>>();
        if (!TryComp<BodyComponent>(uid, out var comp))
            return unfitted;

        Entity<BodyComponent> body = (uid, comp);
        foreach (var (slot, choice) in profile.Augments)
        {
            if (!_proto.TryIndex(choice.Augment, out var augment))
                continue;

            var fitted = augment.Kind switch
            {
                WGAugmentKind.Limb => FindLimb(body, slot) is { } limb && Prototype(limb.Id) == augment.Part?.Id,
                WGAugmentKind.Organ => OrganIn(body, slot) is { } organ && Prototype(organ) == augment.Organ?.Id,
                WGAugmentKind.Missing => FindLimb(body, slot) == null,
                _ => true,
            };
            if (!fitted)
                unfitted.Add(choice.Augment);
        }

        var carried = ReadImplants(uid);
        unfitted.UnionWith(profile.AugmentImplants.Where(i => !carried.Contains(i)));
        return unfitted;
    }

    private void OnCryosleep(EntityUid uid, BodyComponent body, CryosleepBeforeMindRemovedEvent args)
    {
        if (args.User is { } user
            && _played.TryGetValue(user, out var played)
            && _mind.TryGetMind(uid, out var mind, out _)
            && mind == played.Mind)
        {
            // A clone going to sleep is not the body the augments were in.
            if (uid != played.Body)
            {
                Forfeit(user, played, "wg-augments-forfeit-dead");
                return;
            }

            // Out of play from here: the stored body is deleted when its player goes to the lobby or it expires.
            SaveBody(user, played, uid);
            _played.Remove(user);
            _asleep[user] = played;
        }
    }

    private void OnCryoWakeUp(EntityUid uid, BodyComponent body, CryosleepWakeUpEvent args)
    {
        if (args.User is { } user && _asleep.Remove(user, out var played) && uid == played.Body)
            _played[user] = played;
    }

    private void OnRoundEnded(RoundEndedEvent args)
    {
        SaveAllAtRoundEnd();
    }

    /// <summary>Saves or forfeits every character in play. One asleep in cryo was saved going in.</summary>
    internal Task SaveAllAtRoundEnd()
    {
        return Task.WhenAll(_played.ToList().Select(p => SaveAtRoundEnd(p.Key, p.Value)).OfType<Task>().ToList());
    }

    /// <summary>
    /// Keeps a character's paid-for augments if it ends the round alive in its own body, or alive with its player
    /// ghosted out of it, and takes them all if it is a corpse, a clone, a ghost role or a new character. One in cryo
    /// kept them going in.
    /// </summary>
    internal Task? SaveAtRoundEnd(NetUserId user, PlayedCharacter played)
    {
        if (_mind.TryGetMind(user, out var mind)
            && mind.Value.Owner == played.Mind
            && !Deleted(played.Body)
            && !_mobState.IsDead(played.Body)
            && (mind.Value.Comp.OwnedEntity == played.Body
                || HasComp<GhostComponent>(mind.Value.Comp.OwnedEntity) && !_mind.TryGetMind(played.Body, out _, out _)))
            return SaveBody(user, played, played.Body);

        return _cryoSleep.HasCryosleepingBody(user) ? null : Forfeit(user, played, "wg-augments-forfeit-dead");
    }

    /// <summary>Writes the cyberware in a body to the profile of the character it belongs to.</summary>
    /// <returns>The profile save, or null if nothing changed.</returns>
    internal Task? SaveBody(NetUserId user, PlayedCharacter played, EntityUid body)
    {
        if (!_players.TryGetSessionById(user, out var session))
            return SaveBodyOffline(user, played, body);

        if (!_preferences.TryGetCachedPreferences(user, out var prefs)
            || prefs.Characters.GetValueOrDefault(played.Slot) is not HumanoidCharacterProfile profile)
            return null;

        // A character it was taken from no longer owns it.
        var updated = Reconcile(user, played, body, profile, out var taken);
        var saves = taken.Select(t => Revoke(t.Key, t.Value)).ToList();
        if (updated != null)
            saves.Add(Save(session, played.Slot, updated));

        return saves.Count == 0 ? null : Task.WhenAll(saves);
    }

    /// <summary>Writes the cyberware in the body of a player who has left to their stored profile.</summary>
    private async Task SaveBodyOffline(NetUserId user, PlayedCharacter played, EntityUid body)
    {
        var revokes = new List<Task>();
        await Queued(user, async () =>
        {
            try
            {
                // A body deleted while the profile loaded leaves the profile as it was.
                var stored = await _db.GetPlayerPreferencesAsync(user, default);
                if (Deleted(body) || stored?.Characters.GetValueOrDefault(played.Slot) is not HumanoidCharacterProfile profile)
                    return;

                var updated = Reconcile(user, played, body, profile, out var taken);
                revokes.AddRange(taken.Select(t => Revoke(t.Key, t.Value)));
                if (updated == null)
                    return;

                await _db.SaveCharacterSlotAsync(user, updated, played.Slot);
                Log.Info($"Offline user {user}'s {profile.Name} kept the augments in their body.");
            }
            catch (Exception e)
            {
                Log.Error($"Failed to save the augments of offline user {user}'s character {played.Slot}: {e}");
            }
        });

        // Not awaited inside the queue: a victim's queue may be waiting on this player's.
        await Task.WhenAll(revokes);
    }

    /// <summary>
    /// The profile with the paid-for cyberware the body carries, and the characters it was taken from in
    /// <paramref name="taken"/>; null if the profile is already in step.
    /// </summary>
    private HumanoidCharacterProfile? Reconcile(NetUserId user,
        PlayedCharacter played,
        EntityUid body,
        HumanoidCharacterProfile profile,
        out Dictionary<WGAugmentOwner, HashSet<ProtoId<WGAugmentPrototype>>> taken)
    {
        var installed = ReadBody(body, played.Spawned, profile.Augments);
        var augments = new Dictionary<WGAugmentSlot, WGAugmentChoice>();
        foreach (var slot in Enum.GetValues<WGAugmentSlot>())
        {
            var picked = profile.Augments.TryGetValue(slot, out var p) ? p : (WGAugmentChoice?) null;
            var spawned = played.Spawned.TryGetValue(slot, out var s) ? s : (WGAugmentChoice?) null;

            // A slot changed in the creator since the spawn keeps the new pick; it reaches the body next round.
            if (picked != spawned)
            {
                if (picked is { } kept)
                    augments[slot] = kept;
            }
            else if (installed.TryGetValue(slot, out var found))
            {
                augments[slot] = found;
            }
            else if (picked is { } unfitted && played.Unfitted.Contains(unfitted.Augment))
            {
                augments[slot] = unfitted;
            }
        }

        // Implants changed in the creator since the spawn are kept as picked; else the body's are, with the ones
        // that never went in.
        var carried = ReadImplants(body);
        var implants = WGAugmentRules.ValidateImplants(profile.Species,
            augments,
            profile.AugmentImplants.SetEquals(played.SpawnedImplants)
                ? carried.Concat(played.SpawnedImplants.Where(played.Unfitted.Contains))
                : profile.AugmentImplants,
            profile.TraitPreferences,
            _proto,
            _factory,
            profile.Biotype);

        // What the body carried and the character keeps is owned now; what it spawned with and lost is not.
        var owned = profile.AugmentUnlocks.Concat(installed.Values.Select(c => c.Augment)).Concat(carried).ToHashSet();
        var unlocks = profile.AugmentUnlocks.ToHashSet();
        unlocks.ExceptWith(played.Spawned.Values.Select(c => c.Augment)
            .Concat(played.SpawnedImplants)
            .Where(a => !played.Unfitted.Contains(a)));
        unlocks.UnionWith(augments.Values.Select(c => c.Augment).Concat(implants)
            .Where(a => owned.Contains(a) && _proto.TryIndex(a, out var augment) && augment.Price > 0));

        // Only paid-for hardware outlasts the round, whoever paid for it; what the round put in unpaid ends with it.
        var owner = new WGAugmentOwner(user, played.Slot);
        var inBody = installed.Values.Select(c => c.Augment).Concat(carried).ToHashSet();
        var claims = new Dictionary<WGAugmentOwner, HashSet<ProtoId<WGAugmentPrototype>>>();
        taken = claims;
        unlocks.RemoveWhere(u => !ClaimPaid(body, u, inBody.Contains(u), owner, profile, claims));
        ReleasePaid(body, owner);

        bool Kept(ProtoId<WGAugmentPrototype> id) => unlocks.Contains(id) || _proto.TryIndex(id, out var a) && a.Price == 0;
        foreach (var slot in augments.Where(a => !Kept(a.Value.Augment)).Select(a => a.Key).ToList())
            augments.Remove(slot);
        implants = WGAugmentRules.ValidateImplants(profile.Species,
            augments,
            implants.Where(Kept).ToHashSet(),
            profile.TraitPreferences,
            _proto,
            _factory,
            profile.Biotype);

        if (augments.Count == profile.Augments.Count
            && augments.All(a => profile.Augments.TryGetValue(a.Key, out var c) && c == a.Value)
            && implants.SetEquals(profile.AugmentImplants)
            && unlocks.SetEquals(profile.AugmentUnlocks)
            && profile.AugmentFound.Count == 0)
            return null;

        return profile.WithAugments(augments)
            .WithAugmentImplants(implants)
            .WithAugmentUnlocks(unlocks)
            .WithAugmentFound(new HashSet<ProtoId<WGAugmentPrototype>>());
    }

    /// <summary>
    /// The augments a body carries in its slots: each slot's part or organ that is one. A limb keeps the style it was
    /// picked with, else gets the one trait limbs wear. Missing limbs and plating are kept as picked.
    /// </summary>
    internal Dictionary<WGAugmentSlot, WGAugmentChoice> ReadBody(EntityUid uid,
        IReadOnlyDictionary<WGAugmentSlot, WGAugmentChoice> spawned,
        IReadOnlyDictionary<WGAugmentSlot, WGAugmentChoice> picked)
    {
        var found = new Dictionary<WGAugmentSlot, WGAugmentChoice>();
        if (!TryComp<BodyComponent>(uid, out var bodyComp))
            return found;

        Entity<BodyComponent> body = (uid, bodyComp);
        foreach (var augment in _proto.EnumeratePrototypes<WGAugmentPrototype>().OrderBy(a => a.Order).ThenBy(a => a.ID))
        {
            var slot = augment.Slot;
            if (augment.Kind == WGAugmentKind.Upgrade || found.ContainsKey(slot))
                continue;

            var present = augment.Kind switch
            {
                WGAugmentKind.Limb => FindLimb(body, slot) is { } limb
                                      && Prototype(limb.Id) == augment.Part?.Id
                                      && !IsStockChild(limb.Id),
                WGAugmentKind.Organ => OrganIn(body, slot) is { } organ && Prototype(organ) == augment.Organ?.Id,
                WGAugmentKind.Missing => FindLimb(body, slot) == null && Picked(spawned, slot, augment) != null,
                _ => Picked(spawned, slot, augment) != null,
            };
            if (!present)
                continue;

            var style = Picked(spawned, slot, augment)?.Style ?? Picked(picked, slot, augment)?.Style;
            if (style == null && augment.Styled && _proto.TryIndex(TraitLimbStyle, out var traitStyle)
                && WGAugmentRules.StyleFits(traitStyle, slot))
                style = TraitLimbStyle;
            found[slot] = new WGAugmentChoice(augment.ID, style);
        }

        return found;
    }

    /// <summary>The implants a body carries: every upgrade whose components its part or organ has.</summary>
    internal HashSet<ProtoId<WGAugmentPrototype>> ReadImplants(EntityUid uid)
    {
        var found = new HashSet<ProtoId<WGAugmentPrototype>>();
        foreach (var augment in _proto.EnumeratePrototypes<WGAugmentPrototype>())
        {
            if (augment.Kind == WGAugmentKind.Upgrade
                && Surgeon(augment) is { } surgeon
                && _autoSurgeon.WgHasUpgrade(uid, surgeon))
                found.Add(augment.ID);
        }

        return found;
    }

    /// <summary>Every augment in a body, in slot order with a part's implants after it: what a scan shows.</summary>
    public List<ProtoId<WGAugmentPrototype>> ReadInstalled(EntityUid uid)
    {
        var none = new Dictionary<WGAugmentSlot, WGAugmentChoice>();
        return ReadBody(uid, none, none).Values.Select(choice => choice.Augment)
            .Concat(ReadImplants(uid))
            .Select(id => _proto.Index(id))
            .OrderBy(augment => augment.Slot)
            .ThenBy(augment => augment.Kind == WGAugmentKind.Upgrade)
            .ThenBy(augment => augment.ID)
            .Select(augment => new ProtoId<WGAugmentPrototype>(augment.ID))
            .ToList();
    }

    private static WGAugmentChoice? Picked(IReadOnlyDictionary<WGAugmentSlot, WGAugmentChoice> choices,
        WGAugmentSlot slot,
        WGAugmentPrototype augment)
    {
        return choices.TryGetValue(slot, out var choice) && choice.Augment == augment.ID ? choice : null;
    }

    private string? Prototype(EntityUid uid) => MetaData(uid).EntityPrototype?.ID;

    /// <summary>Whether a hand or foot is the one its limb comes with, so it isn't an augment of its own.</summary>
    private bool IsStockChild(EntityUid part)
    {
        return _body.GetParentPartOrNull(part) is { } parent
               && TryComp<GenerateChildPartComponent>(parent, out var generate)
               && generate.Id.Id == Prototype(part);
    }

    /// <summary>The organ in an organ slot, if the body has one there.</summary>
    private EntityUid? OrganIn(Entity<BodyComponent> body, WGAugmentSlot slot)
    {
        if (WGAugmentRules.OrganSlot(slot) is not { } organSlot)
            return null;

        var partType = WGAugmentRules.PartSlot(slot) == "head" ? BodyPartType.Head : BodyPartType.Torso;
        if (_body.GetBodyChildrenOfType(body, partType, body.Comp).FirstOrDefault() is not { Id.Valid: true } part
            || !_containers.TryGetContainer(part.Id, SharedBodySystem.GetOrganContainerId(organSlot), out var container))
            return null;

        return container.ContainedEntities.FirstOrDefault() is { Valid: true } organ ? organ : null;
    }

    /// <summary>
    /// A player respawning with <c>ghostrespawn</c> instead of cryo leaves their character behind, as if cloned: it
    /// loses its augments and their unlocks, so priced ones have to be bought again.
    /// </summary>
    /// <returns>The profile save, or null if nothing was taken.</returns>
    public Task? ForfeitAugments(ICommonSession session)
    {
        if (_cryoSleep.HasCryosleepingBody(session.UserId) || !_played.TryGetValue(session.UserId, out var played))
            return null;

        return Forfeit(session.UserId, played, "wg-augments-forfeit");
    }

    /// <summary>
    /// Takes the augments a character spawned with and their unlocks off it, online or not. Whoever takes its
    /// hardware off the body it left owns it. What was picked in the creator since the spawn never reached the body,
    /// so it stays.
    /// </summary>
    /// <returns>The profile save, or null if nothing was taken.</returns>
    internal Task? Forfeit(NetUserId user, PlayedCharacter played, string message)
    {
        _played.Remove(user);
        ReleasePaid(null, new WGAugmentOwner(user, played.Slot));
        if (!_players.TryGetSessionById(user, out var session))
            return Queued(user, () => ForfeitOffline(user, played));

        if (_preferences.GetPreferencesOrNull(user)?.Characters.GetValueOrDefault(played.Slot) is not HumanoidCharacterProfile profile
            || Stripped(profile, played) is not { } stripped)
            return null;

        _chat.DispatchServerMessage(session, Loc.GetString(message, ("name", profile.Name)));
        Log.Info($"User {user}'s {profile.Name} lost their augments ({message}).");
        return Save(session, played.Slot, stripped);
    }

    private async Task ForfeitOffline(NetUserId user, PlayedCharacter played)
    {
        try
        {
            var stored = await _db.GetPlayerPreferencesAsync(user, default);
            if (stored?.Characters.GetValueOrDefault(played.Slot) is HumanoidCharacterProfile profile
                && Stripped(profile, played) is { } stripped)
            {
                await _db.SaveCharacterSlotAsync(user, stripped, played.Slot);
                Log.Info($"Offline user {user}'s {profile.Name} lost their augments.");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to take the augments off offline user {user}'s character {played.Slot}: {e}");
        }
    }

    /// <summary>
    /// The profile without the augments and implants the character's body spawned with, or their unlocks; null if it
    /// has none of them to lose.
    /// </summary>
    private static HumanoidCharacterProfile? Stripped(HumanoidCharacterProfile profile, PlayedCharacter played)
    {
        var spawned = played.Spawned.Values.Select(c => c.Augment)
            .Concat(played.SpawnedImplants)
            .Where(a => !played.Unfitted.Contains(a))
            .ToHashSet();
        if (!WGAugmentRules.Picked(profile).Concat(profile.AugmentUnlocks).Any(spawned.Contains))
            return null;

        return profile.WithAugments(profile.Augments.Where(a => !spawned.Contains(a.Value.Augment)).ToDictionary(a => a.Key, a => a.Value))
            .WithAugmentImplants(profile.AugmentImplants.Where(i => !spawned.Contains(i)))
            .WithAugmentUnlocks(profile.AugmentUnlocks.Where(u => !spawned.Contains(u)));
    }

    private async Task Save(ICommonSession session, int slot, HumanoidCharacterProfile profile)
    {
        try
        {
            await _preferences.SetProfile(session.UserId, slot, profile);
            _preferences.ResendPreferences(session.UserId);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save the augments of {session.UserId}'s {profile.Name}: {e}");
        }
    }
}
