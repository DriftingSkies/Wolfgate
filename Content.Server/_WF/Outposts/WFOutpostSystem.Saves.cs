using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.Outposts;
using Content.Shared.Cargo.Components;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Stacks;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Saving: the owner writes their outpost into one of their account's save slots at its console, to bring it back in a
/// later round. A save holds the whole grid as gzip'd YAML, with what its parts were worth.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;

    /// <summary>A save is loaded for what its parts are worth, and half again.</summary>
    public const float LoadMarkup = 1.5f;

    private static readonly EntProtoId PlatePrototype = "WFOutpostFoundationPlate";

    /// <summary>Each account's save slots as last read, empty ones null.</summary>
    private readonly Dictionary<NetUserId, WFStoredOutpostSave?[]> _slots = new();

    /// <summary>Accounts with a save, delete or load under way; the Save tab waits for it.</summary>
    private readonly HashSet<NetUserId> _busy = new();

    private readonly HashSet<NetUserId> _reading = new();

    private TimeSpan _nextAutosaveCheck;

    /// <summary>Whether an account has a save, delete or load under way.</summary>
    public bool IsBusy(NetUserId user)
    {
        return _busy.Contains(user) || _reading.Contains(user);
    }

    /// <summary>An account's save slots as last read, or null if they haven't been read yet.</summary>
    public WFStoredOutpostSave?[]? GetSlots(NetUserId user)
    {
        if (_slots.TryGetValue(user, out var slots))
            return slots;

        if (_reading.Add(user))
            _ = ReadSlots(user);
        return null;
    }

    /// <summary>Drops an account's slots as last read, so the next look reads them again.</summary>
    public void ForgetSlots(NetUserId user)
    {
        _slots.Remove(user);
    }

    private async Task ReadSlots(NetUserId user)
    {
        try
        {
            var slots = new WFStoredOutpostSave?[WFOutpostLimits.SaveSlots];
            foreach (var save in await _db.GetOutpostSavesAsync(user))
            {
                if (save.Slot >= 0 && save.Slot < slots.Length)
                    slots[save.Slot] = save;
            }

            _slots[user] = slots;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read the outpost saves of {user}: {e}");
        }
        finally
        {
            _reading.Remove(user);
            RefreshOwnerUi(user);
        }
    }

    /// <summary>Whether a save can't be loaded this round: it was saved or loaded in it.</summary>
    public bool IsInUse(WFStoredOutpostSave save)
    {
        return save.SavedRound == _ticker.RoundId || save.LoadedRound == _ticker.RoundId;
    }

    /// <summary>What a save costs to load.</summary>
    public static int LoadPrice(int appraisal)
    {
        return (int) Math.Ceiling(appraisal * LoadMarkup);
    }

    /// <summary>
    /// An outpost's appraisal as its save stores it: scaled so that <see cref="LoadPrice"/> charges the outpost's own markup,
    /// lower for one with heritage.
    /// </summary>
    public int LoadAppraisal(Entity<MapGridComponent> grid)
    {
        var markup = new WFOutpostMarkupEvent(LoadMarkup);
        RaiseLocalEvent(grid, ref markup);
        return (int) Math.Ceiling(Appraise(grid) * markup.Markup / LoadMarkup);
    }

    /// <summary>
    /// Whether a save keeps an entity: players, mobs, cash, deeds, ghost roles, raid loot and what a hangar ship's strongbox
    /// keeps are left out of its value, and taken off the copy when it is loaded.
    /// </summary>
    public bool IsKept(EntityUid uid)
    {
        return IsKeptAnywhere(uid) && !HasComp<Content.Shared._WF.Hideouts.WFShipSaveOriginComponent>(uid);
    }

    /// <summary>Whether any save keeps an entity: what <see cref="IsKept"/> keeps, and what a hangar ship's strongbox keeps.</summary>
    public bool IsKeptAnywhere(EntityUid uid)
    {
        return !HasComp<ActorComponent>(uid)
               && !HasComp<MobStateComponent>(uid)
               && !HasComp<CashComponent>(uid)
               && !HasComp<ShuttleDeedComponent>(uid)
               && !HasComp<GhostRoleComponent>(uid)
               && !HasComp<GhostTakeoverAvailableComponent>(uid)
               && !HasComp<WFRaidLootComponent>(uid);
    }

    /// <summary>What an outpost's parts are worth: what stands on it that a save keeps, and the plates its floor grew by.</summary>
    public int Appraise(Entity<MapGridComponent> grid)
    {
        return Value(grid, IsKept);
    }

    /// <summary>What one foundation plate is worth; the prototype's estimate is for a whole default stack of them.</summary>
    public double PlatePrice()
    {
        var proto = _proto.Index(PlatePrototype);
        var price = _pricing.GetEstimatedPrice(proto);
        return proto.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory) && stack.Count > 1 ? price / stack.Count : price;
    }

    /// <summary>What the entities on an outpost that pass <paramref name="counted"/> are worth, with its added plates.</summary>
    private int Value(Entity<MapGridComponent> grid, Func<EntityUid, bool> counted)
    {
        var founding = CompOrNull<WFOutpostComponent>(grid)?.FoundingTiles ?? 0;
        var plate = PlatePrice();
        var plates = Math.Max(0, CountTiles(grid) - founding) * plate;
        var ev = new WFOutpostAppraiseEvent(0);
        RaiseLocalEvent(grid, ref ev);
        // A basement's floor was dug for like plates, and what stands down there counts the same.
        if (TryComp<WFOutpostComponent>(grid, out var outpost) && TryGetBasement(outpost, out var basement))
            plates += CountTiles((basement, basement.Comp2)) * plate + _pricing.AppraiseGrid(basement, counted);
        return (int) Math.Ceiling(_pricing.AppraiseGrid(grid, counted) + plates + ev.Value);
    }

    /// <summary>
    /// Saves an outpost into one of its owner's slots, for the owner. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TrySave(EntityUid user, Entity<WFOutpostComponent> outpost, int slot, out string reason)
    {
        if (!TryComp<ActorComponent>(user, out var actor) || actor.PlayerSession.UserId != outpost.Comp.Owner)
        {
            reason = "wf-outpost-save-not-owner";
            return false;
        }

        return TrySaveInto(outpost, slot, user, out reason);
    }

    /// <summary>
    /// Writes an outpost into a slot. The grid is serialised now, so the save is the outpost as it is this tick; packing
    /// and storing it finish later. Everything the save keeps is stamped (<see cref="WFSaveOriginComponent"/>) first.
    /// </summary>
    private bool TrySaveInto(Entity<WFOutpostComponent> outpost, int slot, EntityUid? user, out string reason)
    {
        var owner = outpost.Comp.Owner;
        if (slot < 0 || slot >= WFOutpostLimits.SaveSlots || IsBusy(owner))
        {
            reason = "wf-outpost-save-busy";
            return false;
        }

        if (HasComp<WFOutpostPendingLoadComponent>(outpost))
        {
            reason = "wf-outpost-save-loading";
            return false;
        }

        if (!TryComp<MapGridComponent>(outpost, out var grid))
        {
            reason = "wf-outpost-save-failed";
            return false;
        }

        var entities = CountAllEntities(outpost);
        if (entities > EntityCap(outpost))
        {
            reason = "wf-outpost-save-too-big";
            return false;
        }

        Stamp(outpost);
        CaptureAccess(outpost);
        _roundSetup.SyncOutpostCrew(outpost);
        var saving = new WFOutpostSavingEvent();
        RaiseLocalEvent(outpost, ref saving);
        var options = SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            LogAutoInclude = null,
            Category = FileCategory.Grid,
        };

        MappingDataNode node;
        try
        {
            FileCategory category;
            (node, category) = _mapLoader.SerializeEntitiesRecursive([outpost.Owner], options);
            if (category != FileCategory.Grid)
                throw new InvalidOperationException($"Saved as {category}, not as a grid.");
            DropRecordKeys(node);
            SerializeDepths(outpost, node, options);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save outpost {ToPrettyString(outpost)}: {e}");
            reason = "wf-outpost-save-failed";
            return false;
        }
        finally
        {
            var saved = new WFOutpostSavedEvent();
            RaiseLocalEvent(outpost, ref saved);
        }

        var save = new WFStoredOutpostSave(slot, Name(outpost), CountAllTiles((outpost, outpost.Comp, grid)), entities,
            LoadAppraisal((outpost, grid)), _ticker.RoundId, 0, DateTime.UtcNow, GetLineage(outpost.Comp));
        outpost.Comp.Slot = slot;
        outpost.Comp.SavedFingerprint = Fingerprint((outpost, grid));
        outpost.Comp.NextAutosave = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(OutpostCVars.AutosaveInterval));
        _busy.Add(owner);
        _ = Store(user, owner, save, node, Name(outpost));

        if (user != null)
        {
            _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
                $"{ToPrettyString(user.Value):user} saved outpost {ToPrettyString(outpost):grid} into slot {slot}, appraised at {save.Appraisal}");
        }
        else
        {
            _adminLog.Add(LogType.WFOutpost, LogImpact.Low,
                $"Outpost {ToPrettyString(outpost):grid} was autosaved into slot {slot}, appraised at {save.Appraisal}");
        }

        reason = string.Empty;
        return true;
    }

    private async Task Store(EntityUid? user, NetUserId owner, WFStoredOutpostSave save, MappingDataNode node, string name)
    {
        var message = "wf-outpost-save-done";
        try
        {
            var packed = await Task.Run(() => _shipyard.WFWriteCopy(node, name) is { } yaml ? Pack(yaml) : null);
            if (packed == null)
            {
                message = "wf-outpost-save-failed";
                return;
            }

            await _db.SetOutpostSaveAsync(owner, save, packed);
            if (_slots.TryGetValue(owner, out var slots))
                slots[save.Slot] = save;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to store outpost {name} for {owner}: {e}");
            message = "wf-outpost-save-failed";
        }
        finally
        {
            _busy.Remove(owner);
            if (user is { } body && !TerminatingOrDeleted(body))
                _popup.PopupEntity(Loc.GetString(message), body, body);
            RefreshOwnerUi(owner);
        }
    }

    /// <summary>Components and the fields in them that hold crew record keys, which point at this round's stations.</summary>
    private static readonly (string Component, string Field)[] RecordKeyFields =
    {
        ("AccessReader", "accessKeys"),
        ("WFShipReaderBackup", "keys"),
        ("StationRecordKeyStorage", "key"),
    };

    /// <summary>
    /// Drops crew record keys from a serialised grid: they name this round's stations, which a later round doesn't
    /// have, and ship access writes the door readers afresh when the outpost loads.
    /// </summary>
    private static void DropRecordKeys(MappingDataNode node)
    {
        if (!node.TryGet("entities", out SequenceDataNode? groups))
            return;

        foreach (var group in groups.OfType<MappingDataNode>())
        {
            if (!group.TryGet("entities", out SequenceDataNode? entities))
                continue;

            foreach (var entity in entities.OfType<MappingDataNode>())
            {
                if (!entity.TryGet("components", out SequenceDataNode? components))
                    continue;

                foreach (var component in components.OfType<MappingDataNode>())
                {
                    if (!component.TryGet("type", out ValueDataNode? type))
                        continue;

                    foreach (var (name, field) in RecordKeyFields)
                    {
                        if (type.Value == name)
                            component.Remove(field);
                    }
                }
            }
        }
    }

    /// <summary>Which outpost this is through its saves and loads; one without a lineage is given one.</summary>
    public Guid GetLineage(WFOutpostComponent outpost)
    {
        if (Guid.TryParse(outpost.Lineage, out var lineage))
            return lineage;

        lineage = Guid.NewGuid();
        outpost.Lineage = lineage.ToString();
        return lineage;
    }

    /// <summary>Calls <paramref name="action"/> on everything on an outpost a save keeps, contents of containers included.</summary>
    private void ForEachKept(EntityUid grid, Action<EntityUid, TransformComponent> action)
    {
        var pending = new Stack<EntityUid>();
        pending.Push(grid);
        while (pending.TryPop(out var uid))
        {
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (!IsKept(child))
                    continue;

                action(child, Transform(child));
                pending.Push(child);
            }
        }
    }

    /// <summary>Marks everything a save keeps as having been saved with an outpost, so it can't also be kept elsewhere.</summary>
    private void Stamp(EntityUid grid)
    {
        ForEachKept(grid, (uid, _) => EnsureComp<WFSaveOriginComponent>(uid));
    }

    /// <summary>
    /// A cheap summary of what stands on an outpost and where, for telling whether it changed since its last save:
    /// its floor, and each kept entity's parent, position, anchoring and stack size.
    /// </summary>
    public int Fingerprint(Entity<MapGridComponent> grid)
    {
        _roundSetup.SyncOutpostCrew(grid);
        var hash = new HashCode();
        hash.Add(CountTiles(grid));
        Action<EntityUid, TransformComponent> add = (uid, xform) =>
        {
            hash.Add(uid);
            hash.Add(xform.ParentUid);
            hash.Add((int) MathF.Round(xform.LocalPosition.X * 4));
            hash.Add((int) MathF.Round(xform.LocalPosition.Y * 4));
            hash.Add(xform.Anchored);
            if (TryComp<StackComponent>(uid, out var stack))
                hash.Add(stack.Count);
        };
        ForEachKept(grid, add);
        if (TryComp<WFOutpostComponent>(grid, out var outpost) && TryGetBasement(outpost, out var basement))
        {
            hash.Add(CountTiles((basement, basement.Comp2)));
            ForEachKept(basement, add);
        }

        if (TryComp<WFShipAccessComponent>(grid, out var access))
        {
            hash.Add(access.Mode);
            hash.Add(access.Locked);
            foreach (var entry in access.AllowList)
            {
                hash.Add(entry.Key);
                hash.Add(entry.Builder);
            }
        }

        if (TryComp<WFOutpostCrewComponent>(grid, out var crew))
        {
            hash.Add(crew.Title);
            hash.Add(crew.Description);
            hash.Add(crew.Access);
            hash.Add(crew.AllowLateJoin);
            foreach (var role in crew.Roles)
            {
                hash.Add(role.Name);
                hash.Add(role.Job);
                hash.Add(role.Capacity);
            }
        }

        var ev = new WFOutpostFingerprintEvent(0);
        RaiseLocalEvent(grid, ref ev);
        hash.Add(ev.Hash);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Autosaves one changed outpost at a time into the slot it was last saved into or loaded from, every
    /// <c>wf.outposts.autosave_interval</c> seconds, a few milliseconds a tick (<see cref="TryBeginSlicedSave"/>). An
    /// outpost never saved is given a free slot where something asks for it (<see cref="EnsureSlot"/>), else left alone.
    /// </summary>
    private void UpdateAutosave()
    {
        UpdateSlicedSave();
        var now = _timing.CurTime;
        if (now < _nextAutosaveCheck || _sliced != null)
            return;

        _nextAutosaveCheck = now + TimeSpan.FromSeconds(1);
        var interval = TimeSpan.FromSeconds(_cfg.GetCVar(OutpostCVars.AutosaveInterval));
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var grid))
        {
            if (outpost.Abandoned || outpost.NextAutosave > now || IsBusy(outpost.Owner)
                || HasComp<WFOutpostPendingLoadComponent>(uid) || !EnsureSlot(uid, outpost) || outpost.Slot is not { } slot)
                continue;

            outpost.NextAutosave = now + interval;
            if (Fingerprint((uid, grid)) == outpost.SavedFingerprint)
                continue;

            // Spread over the ticks to come; an outpost whose sliced saves keep tearing is saved at once instead.
            if (_torn.GetValueOrDefault(uid) >= TornLimit)
            {
                _torn.Remove(uid);
                if (TrySaveInto((uid, outpost), slot, null, out _))
                    break;
            }
            else if (TryBeginSlicedSave((uid, outpost), slot))
            {
                break;
            }
        }
    }

    /// <summary>Saves every changed outpost once more as the round ends.</summary>
    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        if (args.New != GameRunLevel.PostRound)
            return;

        SellAtRoundEnd();

        // The round's last saves are taken at once: nothing is left to stall, and nothing may be left half saved.
        AbortSlicedSave();
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var grid))
        {
            if (!outpost.Abandoned && !TerminatingOrDeleted(uid))
                EnsureSlot(uid, outpost);

            if (outpost.Slot is { } slot && !outpost.Abandoned && !TerminatingOrDeleted(uid)
                && Fingerprint((uid, grid)) != outpost.SavedFingerprint)
                TrySaveInto((uid, outpost), slot, null, out _);
        }
    }

    /// <summary>The prototypes an outpost holds most of, for telling an owner what makes it too big to save.</summary>
    public string DescribeBig(EntityUid grid)
    {
        var counts = new Dictionary<string, int>();
        ForEachKept(grid, (uid, _) =>
        {
            var name = MetaData(uid).EntityPrototype?.Name ?? MetaData(uid).EntityName;
            counts[name] = counts.GetValueOrDefault(name) + 1;
        });

        return string.Join(", ", counts.OrderByDescending(c => c.Value).Take(3).Select(c => $"{c.Key} x{c.Value}"));
    }

    /// <summary>Empties one of the owner's save slots.</summary>
    public bool TryDeleteSave(EntityUid user, Entity<WFOutpostComponent> outpost, int slot)
    {
        var owner = outpost.Comp.Owner;
        if (!TryComp<ActorComponent>(user, out var actor) || actor.PlayerSession.UserId != owner
            || slot < 0 || slot >= WFOutpostLimits.SaveSlots || IsBusy(owner)
            || GetSlots(owner) is not { } slots || slots[slot] == null)
            return false;

        _busy.Add(owner);
        _ = Delete(owner, slot);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"{ToPrettyString(user):user} deleted outpost save slot {slot}");
        return true;
    }

    private async Task Delete(NetUserId owner, int slot)
    {
        try
        {
            await _db.DeleteOutpostSaveAsync(owner, slot);
            if (_slots.TryGetValue(owner, out var slots))
                slots[slot] = null;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to delete outpost save slot {slot} of {owner}: {e}");
        }
        finally
        {
            _busy.Remove(owner);
            RefreshOwnerUi(owner);
        }
    }

    private List<WFOutpostSaveSlot?>? GetSlotViews(NetUserId owner)
    {
        if (GetSlots(owner) is not { } slots)
            return null;

        var views = new List<WFOutpostSaveSlot?>();
        foreach (var save in slots)
        {
            views.Add(save == null
                ? null
                : new WFOutpostSaveSlot(save.Name, save.Tiles, save.Entities, LoadPrice(save.Appraisal), save.Saved, IsInUse(save)));
        }

        return views;
    }

    /// <summary>Shows an account's saves afresh on its outpost's console and in its character editor.</summary>
    public void RefreshOwnerUi(NetUserId owner)
    {
        if (TryGetOutpost(owner, out var outpost) && TryComp<WFOutpostConsoleComponent>(outpost.Comp.Console, out var console))
            UpdateUi((outpost.Comp.Console, console));
        SendSaves(owner);
    }

    private void OnSaveMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostSaveMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost))
            return;

        if (!TrySave(args.Actor, (grid, outpost), args.Slot, out var reason))
        {
            var text = reason == "wf-outpost-save-too-big"
                ? Loc.GetString(reason, ("count", CountAllEntities((grid, outpost))), ("max", EntityCap(grid)),
                    ("big", DescribeBig(grid)))
                : Loc.GetString(reason);
            _popup.PopupEntity(text, args.Actor, args.Actor);
        }
        UpdateUi(console);
    }

    private void OnDeleteSaveMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostDeleteSaveMessage args)
    {
        if (console.Comp.Outpost is { } grid && TryComp<WFOutpostComponent>(grid, out var outpost)
            && TryDeleteSave(args.Actor, (grid, outpost), args.Slot))
            UpdateUi(console);
    }

    /// <summary>Gzips a save's YAML as small as it goes.</summary>
    public static byte[] Pack(string yaml)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
        {
            var bytes = Encoding.UTF8.GetBytes(yaml);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>A save's YAML from its gzip'd bytes.</summary>
    public static string Unpack(byte[] packed)
    {
        using var input = new MemoryStream(packed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
