using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Hideouts.Shipwright;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.ShipAccess;
using Content.Server._WF.Shipyard;
using Content.Server.Cargo.Systems;
using Content.Server.Database;
using Content.Server.Storage.Components;
using Content.Server.GameTicking;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Outposts;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Hideouts;

/// <summary>
/// The hangar: the owner registers one ship to their account, and it is saved every time it lands on the outpost's hangar
/// pad. It comes back on the pad when the outpost next lands. A destroyed ship is lost; an insurance policy keeps the
/// ship as it stood when it was insured, and a claim puts that ship on the pad for a deductible.
/// </summary>
public sealed partial class WFHideoutSystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private UsedShipReinitSystem _reinit = default!;
    [Dependency] private WFLandingPadSystem _pads = default!;
    [Dependency] private WFShipAccessServerSystem _shipAccess = default!;
    [Dependency] private WFShipAccessSystem _access = default!;

    /// <summary>The widest a hangar's pad goes; a shipwright bay goes past a plain pad's limit for medium frames.</summary>
    private const int MaxBay = 25;

    /// <summary>The insurance terms of an outpost with no level that sets them.</summary>
    private const float DefaultPremium = 0.25f;
    private const float DefaultDeductible = 0.2f;

    /// <summary>Each account's hangar ship as the database has it; an account not listed hasn't been read yet.</summary>
    private readonly Dictionary<NetUserId, WFStoredHideoutShip?> _rows = new();

    private readonly HashSet<NetUserId> _reading = new();

    /// <summary>Accounts with a write or a restore under way.</summary>
    private readonly HashSet<NetUserId> _busy = new();

    /// <summary>Each account's registered hull this round.</summary>
    private readonly Dictionary<NetUserId, EntityUid> _hulls = new();

    /// <summary>Accounts whose hangar ship this round is a shipwright ship, kept after the hull goes for a claim to find its bay.</summary>
    private readonly HashSet<NetUserId> _shipwright = new();

    /// <summary>True while a restored ship is set on its pad, which isn't a landing to save.</summary>
    private bool _restoring;

    private void InitializeHangar()
    {
        SubscribeLocalEvent<WFHideoutHangarComponent, WFPadDockedEvent>(OnDocked);
        SubscribeLocalEvent<WFHideoutShipComponent, EntityTerminatingEvent>(OnShipTerminating);
        SubscribeLocalEvent<ShipSoldEvent>(OnShipSold);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _hulls.Clear();
        _rows.Clear();
        _shipwright.Clear();
    }

    /// <summary>
    /// A hangar pad standing on an outpost: a shipwright bay's when <paramref name="shipwright"/> is true, a faction
    /// hangar's when it is false, either when it is null.
    /// </summary>
    public bool TryGetHangar(EntityUid outpost, out Entity<WFLandingPadComponent> pad, bool? shipwright = null)
    {
        var query = EntityQueryEnumerator<WFHideoutHangarComponent, WFLandingPadComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var comp, out var xform))
        {
            if (xform.GridUid != outpost || !xform.Anchored
                || shipwright is { } bay && HasComp<WFShipwrightBayComponent>(uid) != bay)
                continue;

            pad = (uid, comp);
            return true;
        }

        pad = default;
        return false;
    }

    /// <summary>The account's registered hull this round, if it still exists.</summary>
    public bool TryGetShip(NetUserId owner, out EntityUid hull)
    {
        return _hulls.TryGetValue(owner, out hull) && !TerminatingOrDeleted(hull);
    }

    /// <summary>Sets the hangar pads of an outpost to the widest its built levels allow.</summary>
    private void ApplyHangar(EntityUid outpost)
    {
        if (!TryComp<WFHideoutComponent>(outpost, out var hideout))
            return;

        if (TryGetHangar(outpost, out var bay, shipwright: true))
            Widen(bay, Built(hideout).Select(level => level.ShipwrightSize).DefaultIfEmpty(0).Max());

        if (TryGetHangar(outpost, out var pad, shipwright: false))
            Widen(pad, Built(hideout).Select(level => level.HangarSize).DefaultIfEmpty(0).Max());
    }

    /// <summary>Sets how wide a hangar pad may go, and widens it to that while nothing is on it.</summary>
    private void Widen(Entity<WFLandingPadComponent> pad, int widest)
    {
        if (widest < WFLandingPadComponent.MinSize || pad.Comp.Widest == widest)
            return;

        pad.Comp.Widest = Math.Min(widest, MaxBay);
        if (pad.Comp.Parked == null && !pad.Comp.Guiding)
            pad.Comp.Size = pad.Comp.Widest;
        Dirty(pad);
    }

    private (float Premium, float Deductible) Terms(WFHideoutComponent hideout)
    {
        float? premium = null;
        float? deductible = null;
        foreach (var level in Built(hideout))
        {
            if (level.InsurancePremium is { } p && (premium == null || p < premium))
                premium = p;
            if (level.InsuranceDeductible is { } d && (deductible == null || d < deductible))
                deductible = d;
        }

        return (premium ?? DefaultPremium, deductible ?? DefaultDeductible);
    }

    private static int Share(int value, float share)
    {
        return (int) Math.Ceiling(value * share);
    }

    private WFHideoutHangarView HangarView(Entity<WFHideoutComponent, WFOutpostComponent> outpost)
    {
        var owner = outpost.Comp2.Owner;
        if (!_rows.TryGetValue(owner, out var row))
            _ = ReadRow(owner);

        var hasPad = TryGetHangar(outpost, out var pad, shipwright: false);
        var status = WFHideoutShipStatus.None;
        if (row != null)
        {
            status = row.Lost ? WFHideoutShipStatus.Lost
                : !TryGetShip(owner, out var hull) ? WFHideoutShipStatus.Stored
                : IsHome(owner, hull) ? WFHideoutShipStatus.Parked
                : WFHideoutShipStatus.Away;
        }

        string? unregistered = null;
        if (hasPad && pad.Comp.Parked is { } parked && !TerminatingOrDeleted(parked) && !HasComp<WFHideoutShipComponent>(parked))
            unregistered = Name(parked);

        var (premium, deductible) = Terms(outpost.Comp1);
        return new WFHideoutHangarView(hasPad, status, row?.Name, row?.Value ?? 0, row?.InsuredValue ?? 0,
            Share(row?.Value ?? 0, premium), Share(row?.InsuredValue ?? 0, deductible), unregistered,
            _busy.Contains(owner) || !_rows.ContainsKey(owner));
    }

    private async Task ReadRow(NetUserId owner)
    {
        if (!_reading.Add(owner))
            return;

        try
        {
            _rows[owner] = await _db.GetHideoutShipAsync(owner);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read the hangar ship of {owner}: {e}");
        }
        finally
        {
            _reading.Remove(owner);
            RefreshOwner(owner);
        }
    }

    private void RefreshOwner(NetUserId owner)
    {
        if (_outposts.TryGetOutpost(owner, out var outpost))
            _outposts.RefreshConsole(outpost);
    }

    /// <summary>Whether the user holds the hull: its deed is on their card, or it is registered to their account.</summary>
    public bool Holds(EntityUid user, NetUserId account, EntityUid hull)
    {
        if (_access.TryGetCard(user, out var card) && _shipyard.TryGetDeedShip(card, out var deeded) && deeded == hull)
            return true;

        return TryComp<WFShipAccessComponent>(hull, out var access) && access.OwnerUsers.Contains(account);
    }

    /// <summary>
    /// Registers the hull parked in the hangar as the owner's hangar ship and saves it. <paramref name="reason"/> is a loc
    /// id when it fails.
    /// </summary>
    public bool TryRegister(EntityUid user, Entity<WFOutpostComponent> outpost, out string reason)
    {
        var owner = outpost.Comp.Owner;
        if (!_outposts.IsOwnerOf(user, outpost.Comp) || _busy.Contains(owner) || !_rows.TryGetValue(owner, out var row))
        {
            reason = "wf-hideout-ship-busy";
            return false;
        }

        if (row != null)
        {
            reason = "wf-hideout-ship-already";
            return false;
        }

        if (!TryGetHangar(outpost, out var pad, shipwright: false) || pad.Comp.Parked is not { } hull || TerminatingOrDeleted(hull))
        {
            reason = "wf-hideout-ship-none-parked";
            return false;
        }

        if (!Holds(user, owner, hull))
        {
            reason = "wf-hideout-ship-not-yours";
            return false;
        }

        if (!IsFactionHull(user, hull, out reason))
            return false;

        return RegisterHull(user, outpost, hull, out reason);
    }

    /// <summary>
    /// Whether a faction hangar takes the hull: a stock shipyard hull, not a shipwright ship, and one the user's company
    /// may fly if its listing names companies.
    /// </summary>
    public bool IsFactionHull(EntityUid user, EntityUid hull, out string reason)
    {
        if (HasComp<WFShipwrightShipComponent>(hull))
        {
            reason = "wf-hideout-ship-shipwright-only";
            return false;
        }

        if (!TryComp<VesselComponent>(hull, out var vessel) || !_proto.TryIndex<VesselPrototype>(vessel.VesselId, out var listing))
        {
            reason = "wf-hideout-ship-not-stock";
            return false;
        }

        if (listing.Company.Count > 0)
        {
            string? company = _access.TryGetCard(user, out var card) && TryComp<IdCardComponent>(card, out var id) ? id.CompanyName.Id : null;
            if (string.IsNullOrEmpty(company) || !listing.Company.Contains(company))
            {
                reason = "wf-hideout-ship-wrong-faction";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Registers a hull as the owner's hangar ship and saves it, once the bay it stands on has accepted it. The owner's
    /// slot has to be free. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool RegisterHull(EntityUid user, Entity<WFOutpostComponent> outpost, EntityUid hull, out string reason)
    {
        var owner = outpost.Comp.Owner;
        if (!_outposts.IsOwnerOf(user, outpost.Comp) || _busy.Contains(owner) || !_rows.TryGetValue(owner, out var row))
        {
            reason = "wf-hideout-ship-busy";
            return false;
        }

        if (row != null)
        {
            reason = "wf-hideout-ship-already";
            return false;
        }

        Mark(hull, outpost);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{ToPrettyString(user):user} registered {ToPrettyString(hull):grid} as their hangar ship");
        if (TrySave(hull, owner, false, out reason))
            return true;

        // A hull the bay won't certify is still registered; it saves once it passes.
        if (reason != "wf-hideout-ship-save-failed")
        {
            _rows[owner] = new WFStoredHideoutShip(Name(hull), 0, 0, DateTime.UtcNow, false, 0, false);
            return true;
        }

        return false;
    }

    /// <summary>Saves the account's hangar ship now as its last landing, if it is home; used once a shipwright job is done.</summary>
    public bool TrySaveHome(NetUserId owner, out string reason)
    {
        if (!TryGetShip(owner, out var hull) || !IsHome(owner, hull))
        {
            reason = "wf-hideout-insure-not-parked";
            return false;
        }

        return TrySave(hull, owner, false, out reason);
    }

    private void Mark(EntityUid hull, Entity<WFOutpostComponent> outpost)
    {
        EnsureComp<WFHideoutShipComponent>(hull).Owner = outpost.Comp.Owner;
        _hulls[outpost.Comp.Owner] = hull;
        if (HasComp<WFShipwrightShipComponent>(hull))
            _shipwright.Add(outpost.Comp.Owner);
        else
            _shipwright.Remove(outpost.Comp.Owner);
        if (HasComp<WFShipAccessComponent>(hull))
            _shipAccess.AddOwnerUser(hull, outpost.Comp.Owner);
        else
            _shipAccess.SetupRegisteredShip(hull, outpost.Comp.OwnerName, new[] { outpost.Comp.Owner });
    }

    /// <summary>Gives up the owner's hangar ship: the hull, if it is out, stays as an ordinary ship, and its saves are deleted.</summary>
    public bool TryUnregister(EntityUid user, Entity<WFOutpostComponent> outpost, out string reason)
    {
        var owner = outpost.Comp.Owner;
        if (!_outposts.IsOwnerOf(user, outpost.Comp) || _busy.Contains(owner) || !_rows.TryGetValue(owner, out var row) || row == null)
        {
            reason = "wf-hideout-ship-busy";
            return false;
        }

        Forget(owner);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{ToPrettyString(user):user} gave up their hangar ship {row.Name}");
        reason = string.Empty;
        return true;
    }

    /// <summary>Drops an account's hangar ship from the round and the database.</summary>
    private void Forget(NetUserId owner)
    {
        if (_hulls.Remove(owner, out var hull) && !TerminatingOrDeleted(hull))
            RemComp<WFHideoutShipComponent>(hull);

        _rows[owner] = null;
        _ = Write(owner, () => _db.DeleteHideoutShipAsync(owner));
    }

    /// <summary>Runs one database write for an account, keeping its console waiting meanwhile.</summary>
    private async Task Write(NetUserId owner, Func<Task> write)
    {
        _busy.Add(owner);
        try
        {
            await write();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to write the hangar ship of {owner}: {e}");
        }
        finally
        {
            _busy.Remove(owner);
            RefreshOwner(owner);
        }
    }

    /// <summary>What a save of the hull would keep is worth: the ship and its strongboxes' contents, not its cargo.</summary>
    public int Appraise(EntityUid hull)
    {
        var cargo = new HashSet<EntityUid>(Cargo(hull));
        return (int) Math.Ceiling(_pricing.AppraiseGrid(hull, uid => _outposts.IsKeptAnywhere(uid) && !cargo.Contains(uid)));
    }

    /// <summary>
    /// What a hangar ship doesn't keep: loose items and crates on its floor, and what lockers and other storage hold, but
    /// not what its strongboxes hold or what sits inside machines.
    /// </summary>
    public List<EntityUid> Cargo(EntityUid hull)
    {
        var cargo = new List<EntityUid>();
        var children = Transform(hull).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<WFShipStashComponent>(child))
                continue;

            var xform = Transform(child);
            if (!xform.Anchored && (HasComp<ItemComponent>(child) || HasComp<EntityStorageComponent>(child)))
            {
                AddWithContents(child, cargo);
                continue;
            }

            if (TryComp<EntityStorageComponent>(child, out var closet))
            {
                foreach (var held in closet.Contents.ContainedEntities)
                    AddWithContents(held, cargo);
            }

            if (TryComp<StorageComponent>(child, out var storage))
            {
                foreach (var held in storage.Container.ContainedEntities)
                    AddWithContents(held, cargo);
            }
        }

        return cargo;
    }

    private void AddWithContents(EntityUid uid, List<EntityUid> list)
    {
        list.Add(uid);
        if (!TryComp<ContainerManagerComponent>(uid, out var manager))
            return;

        foreach (var container in manager.Containers.Values)
        {
            foreach (var held in container.ContainedEntities)
                AddWithContents(held, list);
        }
    }

    /// <summary>Takes a loaded ship's cargo off it, so only the ship and its strongboxes come back.</summary>
    private void StripCargo(EntityUid hull)
    {
        foreach (var uid in Cargo(hull))
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }
    }

    /// <summary>Stamps what the hull's strongboxes hold, so nothing else keeps it too.</summary>
    private void StampStash(EntityUid hull)
    {
        var children = Transform(hull).ChildEnumerator;
        var held = new List<EntityUid>();
        while (children.MoveNext(out var child))
        {
            if (HasComp<WFShipStashComponent>(child) && TryComp<StorageComponent>(child, out var storage))
            {
                foreach (var item in storage.Container.ContainedEntities)
                    AddWithContents(item, held);
            }
        }

        foreach (var uid in held)
            EnsureComp<WFShipSaveOriginComponent>(uid);
    }

    /// <summary>
    /// Saves the hull as it stands this tick, as the account's last landing or as its insured state; packing and storing
    /// finish later.
    /// </summary>
    private bool TrySave(EntityUid hull, NetUserId owner, bool insured, out string reason)
    {
        if (_busy.Contains(owner))
        {
            reason = "wf-hideout-ship-busy";
            return false;
        }

        var attempt = new WFHideoutShipSaveAttemptEvent();
        RaiseLocalEvent(hull, ref attempt);
        if (attempt.Refusal is { } refusal)
        {
            reason = refusal;
            return false;
        }

        StampStash(hull);
        var value = Appraise(hull);
        if (!_shipyard.TrySaveShipDeferred(hull, new List<EntityUid>(), out var writing))
        {
            reason = "wf-hideout-ship-save-failed";
            return false;
        }

        _ = Store(owner, Name(hull), value, writing, insured);
        reason = string.Empty;
        return true;
    }

    private async Task Store(NetUserId owner, string name, int value, Task<string?> writing, bool insured)
    {
        _busy.Add(owner);
        try
        {
            var packed = await Task.Run(async () => await writing is { } yaml ? WFOutpostSystem.Pack(yaml) : null);
            if (packed == null)
                return;

            _rows.TryGetValue(owner, out var old);
            if (insured)
            {
                await _db.SetHideoutShipInsuredAsync(owner, value, packed);
                if (old != null)
                    _rows[owner] = old with { InsuredValue = value, Insured = true };
            }
            else
            {
                await _db.SetHideoutShipAsync(owner, name, value, _ticker.RoundId, packed);
                _rows[owner] = new WFStoredHideoutShip(name, value, _ticker.RoundId, DateTime.UtcNow, false,
                    old?.InsuredValue ?? 0, old?.Insured ?? false);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to store hangar ship {name} of {owner}: {e}");
        }
        finally
        {
            _busy.Remove(owner);
            RefreshOwner(owner);
        }
    }

    private void OnDocked(Entity<WFHideoutHangarComponent> pad, ref WFPadDockedEvent args)
    {
        if (_restoring || !TryComp<WFHideoutShipComponent>(args.Hull, out var ship)
            || !TryComp<WFOutpostComponent>(Transform(pad).GridUid, out var outpost) || outpost.Owner != ship.Owner)
            return;

        if (!TrySave(args.Hull, ship.Owner, false, out var reason) && reason != "wf-hideout-ship-busy")
            _popup.PopupEntity(Loc.GetString("wf-hideout-ship-not-saved", ("reason", Loc.GetString(reason))), pad, PopupType.MediumCaution);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        if (args.New != GameRunLevel.PostRound)
            return;

        // The round's last word on each ship that is home.
        foreach (var (owner, hull) in _hulls)
        {
            if (!TerminatingOrDeleted(hull) && HasComp<WFPadDockedComponent>(hull) && IsHome(owner, hull))
                TrySave(hull, owner, false, out _);
        }
    }

    /// <summary>Whether the hull rests on a hangar pad of its owner's outpost.</summary>
    public bool IsHome(NetUserId owner, EntityUid hull)
    {
        return _outposts.TryGetOutpost(owner, out var outpost)
               && TryComp<WFPadDockedComponent>(hull, out var docked)
               && HasComp<WFHideoutHangarComponent>(docked.Pad)
               && Transform(docked.Pad).GridUid == outpost.Owner;
    }

    private void OnShipTerminating(Entity<WFHideoutShipComponent> hull, ref EntityTerminatingEvent args)
    {
        var owner = hull.Comp.Owner;
        if (!_hulls.TryGetValue(owner, out var live) || live != hull.Owner)
            return;

        _hulls.Remove(owner);
        // A hull that goes with the round isn't lost; one that goes during it is.
        if (_ticker.RunLevel == GameRunLevel.InRound)
            Lose(owner);
    }

    /// <summary>Notes an account's hangar ship as destroyed: it no longer comes back, short of an insurance claim.</summary>
    public void Lose(NetUserId owner)
    {
        if (!_rows.TryGetValue(owner, out var row) || row == null || row.Lost)
            return;

        _rows[owner] = row with { Lost = true };
        _ = Write(owner, () => _db.SetHideoutShipLostAsync(owner, true));
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"Hangar ship {row.Name} of {owner} was destroyed");
    }

    /// <summary>Whether a hangar write or restore is under way for the account, or its ship hasn't been read yet.</summary>
    public bool IsBusy(NetUserId owner)
    {
        if (!_rows.ContainsKey(owner))
            _ = ReadRow(owner);

        return _busy.Contains(owner) || !_rows.ContainsKey(owner);
    }

    /// <summary>The account's hangar ship as last read, without its grids.</summary>
    public WFStoredHideoutShip? GetStored(NetUserId owner)
    {
        return _rows.GetValueOrDefault(owner);
    }

    private void OnShipSold(ref ShipSoldEvent args)
    {
        // A sold ship doesn't come back next round.
        if (TryComp<WFHideoutShipComponent>(args.Shuttle, out var ship))
            Forget(ship.Owner);
    }

    private void OnSettled(Entity<WFHideoutComponent> outpost, ref WFOutpostSettledEvent args)
    {
        ApplyHangar(outpost);
        if (TryComp<WFOutpostComponent>(outpost, out var settled))
            EnsureHome((outpost, outpost.Comp, settled));
        if (!_cfg.GetCVar(HideoutCVars.Enabled) || !TryComp<WFOutpostComponent>(outpost, out var comp) || TryGetShip(comp.Owner, out _))
            return;

        _ = Restore((outpost, comp), null);
    }

    /// <summary>
    /// Brings the owner's stored ship out onto the hangar pad, as it last landed. A lost ship stays lost.
    /// </summary>
    private async Task Restore(Entity<WFOutpostComponent> outpost, EntityUid? user)
    {
        var owner = outpost.Comp.Owner;
        if (!_busy.Add(owner))
            return;

        var message = "wf-hideout-ship-recalled";
        try
        {
            var read = await _db.GetHideoutShipDataAsync(owner, false);
            _rows[owner] = read?.Ship;
            if (read is not { } stored || stored.Ship.Lost)
            {
                message = "wf-hideout-ship-none-stored";
                return;
            }

            var yaml = await Task.Run(() => WFOutpostSystem.Unpack(stored.Data));
            if (TerminatingOrDeleted(outpost) || TryGetShip(owner, out _))
                return;

            if (!TryPlaceStored(outpost, yaml, out var hull, out var reason))
            {
                message = reason;
                return;
            }

            _metaData.SetEntityName(hull.Value, stored.Ship.Name);
            _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
                $"Hangar ship {stored.Ship.Name} of {owner} came out as {ToPrettyString(hull.Value):grid} on outpost {ToPrettyString(outpost):grid}");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to bring out the hangar ship of {owner}: {e}");
            message = "wf-hideout-ship-save-failed";
        }
        finally
        {
            _busy.Remove(owner);
            if (user is { } body && !TerminatingOrDeleted(body))
                _popup.PopupEntity(Loc.GetString(message), body, body);
            RefreshOwner(owner);
        }
    }

    /// <summary>Loads a stored ship, strips it like any loaded save and rests it on the outpost's hangar pad.</summary>
    private bool TryPlaceStored(Entity<WFOutpostComponent> outpost, string yaml, [NotNullWhen(true)] out EntityUid? hull, out string reason)
    {
        hull = null;
        var shipwright = yaml.Contains("type: WFShipwrightShip");
        if (!TryGetHangar(outpost, out var pad, shipwright) || Transform(outpost).MapUid is not { } map)
        {
            reason = "wf-hideout-ship-no-hangar";
            return false;
        }

        if (!_shipyard.TryAddSavedShip(yaml, out var loaded) || !TryComp<MapGridComponent>(loaded, out var grid))
        {
            reason = "wf-hideout-ship-save-failed";
            return false;
        }

        _shipyard.StripForResale(loaded.Value);
        _outposts.StripUnkept(loaded.Value);
        StripCargo(loaded.Value);
        if (!_pads.TryPlace(loaded.Value, grid, pad, outpost, out var placement, out _, needPower: false))
        {
            Del(loaded.Value);
            reason = "wf-hideout-ship-no-room";
            return false;
        }

        _transform.SetCoordinates(loaded.Value, new EntityCoordinates(map, placement.Value.Position));
        _transform.SetWorldRotation(loaded.Value, placement.Value.Rotation);
        _reinit.ReinitLoadedShip(loaded.Value);
        _restoring = true;
        _pads.Park(loaded.Value, pad);
        _restoring = false;
        Mark(loaded.Value, outpost);
        hull = loaded.Value;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Insures the parked ship as it stands, for a share of its value; a policy already held is replaced.
    /// <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryInsure(EntityUid user, Entity<WFOutpostComponent> outpost, out string reason)
    {
        var owner = outpost.Comp.Owner;
        if (!_outposts.IsOwnerOf(user, outpost.Comp) || _busy.Contains(owner) || !TryComp<WFHideoutComponent>(outpost, out var hideout))
        {
            reason = "wf-hideout-ship-busy";
            return false;
        }

        if (!TryGetShip(owner, out var hull) || !IsHome(owner, hull))
        {
            reason = "wf-hideout-insure-not-parked";
            return false;
        }

        var premium = Share(Appraise(hull), Terms(hideout).Premium);
        if (premium > 0 && !_bank.TryBankWithdraw(user, premium))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        if (!TrySave(hull, owner, true, out reason))
        {
            _bank.TryBankDeposit(user, premium, tax: false);
            return false;
        }

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{ToPrettyString(user):user} insured hangar ship {ToPrettyString(hull):grid} for {premium}");
        return true;
    }

    /// <summary>
    /// Claims on the policy: the hull that is out is written off, and the ship comes back on the hangar pad as it was
    /// insured, for the deductible. The policy is used up. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryClaim(EntityUid user, Entity<WFOutpostComponent> outpost, out string reason)
    {
        var owner = outpost.Comp.Owner;
        if (!_outposts.IsOwnerOf(user, outpost.Comp) || _busy.Contains(owner) || !TryComp<WFHideoutComponent>(outpost, out var hideout)
            || !_rows.TryGetValue(owner, out var row))
        {
            reason = "wf-hideout-ship-busy";
            return false;
        }

        if (row is not { Insured: true })
        {
            reason = "wf-hideout-claim-no-policy";
            return false;
        }

        if (!TryGetHangar(outpost, out var pad, _shipwright.Contains(owner)))
        {
            reason = "wf-hideout-ship-no-hangar";
            return false;
        }

        var hasHull = TryGetShip(owner, out var hull);
        if (pad.Comp.Guiding || pad.Comp.Parked is { } parked && !TerminatingOrDeleted(parked) && (!hasHull || parked != hull))
        {
            reason = "wf-hideout-ship-no-room";
            return false;
        }

        if (hasHull && IsCrewed(hull))
        {
            reason = "wf-hideout-claim-crewed";
            return false;
        }

        var deductible = Share(row.InsuredValue, Terms(hideout).Deductible);
        if (deductible > 0 && !_bank.TryBankWithdraw(user, deductible))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        if (hasHull)
        {
            _hulls.Remove(owner);
            RemComp<WFHideoutShipComponent>(hull);
            Del(hull);
        }

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{ToPrettyString(user):user} claimed on hangar ship {row.Name} for {deductible}");
        _ = Claim(outpost, user);
        reason = string.Empty;
        return true;
    }

    private async Task Claim(Entity<WFOutpostComponent> outpost, EntityUid user)
    {
        var owner = outpost.Comp.Owner;
        await Write(owner, () => _db.ClaimHideoutShipAsync(owner, _ticker.RoundId));
        await Restore(outpost, user);
    }

    /// <summary>Whether a player is aboard the hull.</summary>
    private bool IsCrewed(EntityUid hull)
    {
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == hull)
                return true;
        }

        return false;
    }

    private void OnRegisterMessage(Entity<WFOutpostConsoleComponent> console, ref WFHideoutRegisterShipMessage args)
    {
        if (TryOutpost(console, out var outpost))
            Report(outpost, args.Actor, TryRegister(args.Actor, outpost, out var reason), reason);
    }

    private void OnUnregisterMessage(Entity<WFOutpostConsoleComponent> console, ref WFHideoutUnregisterShipMessage args)
    {
        if (TryOutpost(console, out var outpost))
            Report(outpost, args.Actor, TryUnregister(args.Actor, outpost, out var reason), reason);
    }

    private void OnRecallMessage(Entity<WFOutpostConsoleComponent> console, ref WFHideoutRecallShipMessage args)
    {
        if (TryOutpost(console, out var outpost))
            Report(outpost, args.Actor, TryRecall(args.Actor, outpost, out var reason), reason);
    }

    /// <summary>
    /// Starts bringing the owner's stored ship out onto the hangar pad. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryRecall(EntityUid user, Entity<WFOutpostComponent> outpost, out string reason)
    {
        var owner = outpost.Comp.Owner;
        if (!_outposts.IsOwnerOf(user, outpost.Comp) || _busy.Contains(owner))
        {
            reason = "wf-hideout-ship-busy";
            return false;
        }

        if (TryGetShip(owner, out _) || _rows.GetValueOrDefault(owner) is not { Lost: false })
        {
            reason = "wf-hideout-ship-none-stored";
            return false;
        }

        if (!TryGetHangar(outpost, out _))
        {
            reason = "wf-hideout-ship-no-hangar";
            return false;
        }

        _ = Restore(outpost, user);
        reason = string.Empty;
        return true;
    }

    private void OnInsureMessage(Entity<WFOutpostConsoleComponent> console, ref WFHideoutInsureMessage args)
    {
        if (TryOutpost(console, out var outpost))
            Report(outpost, args.Actor, TryInsure(args.Actor, outpost, out var reason), reason);
    }

    private void OnClaimMessage(Entity<WFOutpostConsoleComponent> console, ref WFHideoutClaimMessage args)
    {
        if (TryOutpost(console, out var outpost))
            Report(outpost, args.Actor, TryClaim(args.Actor, outpost, out var reason), reason);
    }

    private void Report(Entity<WFOutpostComponent> outpost, EntityUid user, bool ok, string reason)
    {
        if (!ok)
            _popup.PopupEntity(Loc.GetString(reason), user, user);
        _outposts.RefreshConsole(outpost);
    }
}
