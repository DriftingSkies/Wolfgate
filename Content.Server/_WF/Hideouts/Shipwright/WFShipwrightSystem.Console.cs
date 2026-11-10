using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Drydock;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Administration.Logs;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.Hideouts.Shipwright;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Database;
using Content.Shared.DeviceLinking;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Hideouts.Shipwright;

/// <summary>
/// The shipwright console: a drydock console on an outpost whose cranes work the ship on its bay's pad. They build a
/// frame from its keel, fit the guns and systems the owner orders, and look over hand-built parts to certify them.
/// </summary>
public sealed partial class WFShipwrightSystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private SharedDeviceLinkSystem _deviceLink = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WFDrydockSystem _drydock = default!;
    [Dependency] private WFLandingPadSystem _pads = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextRefresh;

    private void InitializeConsole()
    {
        SubscribeLocalEvent<WFShipwrightConsoleComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<WFShipwrightConsoleComponent, WFDrydockJobEndedEvent>(OnJobEnded);
        SubscribeLocalEvent<WFShipwrightShipComponent, WFDrydockPartInstalledEvent>(OnPartInstalled);
        SubscribeLocalEvent<WFShipwrightShipComponent, WFDrydockPartInspectedEvent>(OnPartInspected);
        Subs.BuiEvents<WFShipwrightConsoleComponent>(WFShipwrightConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WFShipwrightBuildFrameMessage>(OnBuildFrameMessage);
            subs.Event<WFShipwrightRegisterMessage>(OnRegisterMessage);
            subs.Event<WFShipwrightCertifyMessage>(OnCertifyMessage);
            subs.Event<WFShipwrightRefitMessage>(OnRefitMessage);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;
        var query = EntityQueryEnumerator<WFShipwrightConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, WFShipwrightConsoleUiKey.Key))
                UpdateUi((uid, comp));
        }
    }

    /// <summary>The outpost the console stands on.</summary>
    private bool TryOutpost(EntityUid console, out Entity<WFOutpostComponent> outpost)
    {
        outpost = default;
        if (Transform(console).GridUid is not { } grid || !TryComp<WFOutpostComponent>(grid, out var comp))
            return false;

        outpost = (grid, comp);
        return true;
    }

    /// <summary>The bay pad the console works: the nearest shipwright bay on its grid, in range.</summary>
    public bool TryGetBay(Entity<WFShipwrightConsoleComponent> console, out Entity<WFLandingPadComponent> pad)
    {
        pad = default;
        var xform = Transform(console);
        var at = _transform.GetWorldPosition(xform);
        var best = float.MaxValue;
        var query = EntityQueryEnumerator<WFHideoutHangarComponent, WFLandingPadComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var comp, out var padXform))
        {
            if (padXform.GridUid != xform.GridUid || !padXform.Anchored || HasComp<WFShipwrightBayComponent>(uid) == console.Comp.Faction)
                continue;

            var distance = (_transform.GetWorldPosition(padXform) - at).Length();
            if (distance > console.Comp.BayRange || distance >= best)
                continue;

            best = distance;
            pad = (uid, comp);
        }

        return best < float.MaxValue;
    }

    /// <summary>Links the console to the drydock cranes in range on its grid that no console has, so a laid prefab works at once.</summary>
    public void LinkCranes(Entity<WFShipwrightConsoleComponent> console)
    {
        var xform = Transform(console);
        var at = _transform.GetWorldPosition(xform);
        var query = EntityQueryEnumerator<WFDrydockCraneComponent, TransformComponent>();
        var cranes = new List<EntityUid>();
        while (query.MoveNext(out var uid, out var crane, out var craneXform))
        {
            if (craneXform.GridUid == xform.GridUid && crane.LinkedConsole == null
                && (_transform.GetWorldPosition(craneXform) - at).Length() <= console.Comp.CraneRange)
                cranes.Add(uid);
        }

        foreach (var crane in cranes)
        {
            _deviceLink.SaveLinks(null, console, crane,
                new List<(string, string)> { (WFDrydockConsoleComponent.CranePort, WFDrydockConsoleComponent.CraneReceiverPort) });
        }
    }

    private void OnOpenAttempt(Entity<WFShipwrightConsoleComponent> console, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || !TryOutpost(console, out var outpost) || _outposts.IsOwnerOf(args.User, outpost.Comp))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wf-outpost-console-not-yours"), console, args.User);
    }

    private void OnUiOpened(Entity<WFShipwrightConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        LinkCranes(console);
        _ui.ServerSendUiMessage(console.Owner, WFShipwrightConsoleUiKey.Key, new WFShipwrightCatalogueMessage(Guns(), MountTypes()), args.Actor);
        UpdateUi(console);
    }

    /// <summary>Why the console can't start work now, as a loc id, or null.</summary>
    private string? Blocked(Entity<WFShipwrightConsoleComponent> console, out WFDrydockConsoleComponent? drydock)
    {
        drydock = null;
        if (!TryOutpost(console, out _))
            return "wf-shipwright-not-outpost";
        if (!TryComp(console, out drydock) || !_drydock.TryGetBerth(console, drydock, out _))
            return "wf-shipwright-no-cranes";
        if (!TryGetBay(console, out _))
            return "wf-shipwright-no-bay";
        if (HasComp<WFDrydockJobComponent>(console))
            return "wf-shipwright-working";
        return null;
    }

    public void UpdateUi(Entity<WFShipwrightConsoleComponent> console)
    {
        var state = new WFShipwrightConsoleState();
        state.Blocked = Blocked(console, out _);
        if (TryComp<WFDrydockJobComponent>(console, out var job))
        {
            state.Working = true;
            state.Done = job.Done;
            state.Total = job.Total;
        }

        state.Faction = console.Comp.Faction;
        if (TryOutpost(console, out var outpost))
        {
            var owner = outpost.Comp.Owner;
            state.BayPoints = BayPoints(outpost);
            state.CanBuildFrame = !_hideouts.IsBusy(owner) && _hideouts.GetStored(owner) == null && !_hideouts.TryGetShip(owner, out _);
            if (_players.TryGetSessionById(owner, out var session))
                _bank.TryGetBalance(session, out state.Balance);
            if (TryGetBay(console, out var pad) && pad.Comp.Parked is { } hull && !TerminatingOrDeleted(hull))
                state.Hull = console.Comp.Faction ? FactionHullView(hull, outpost) : HullView(hull, outpost);
        }

        _ui.SetUiState(console.Owner, WFShipwrightConsoleUiKey.Key, state);
    }

    private WFShipwrightHullView HullView(EntityUid hull, Entity<WFOutpostComponent> outpost)
    {
        var survey = Survey(hull, outpost);
        var mounts = new List<WFShipwrightMountView>();
        foreach (var (hardpoint, comp) in Hardpoints(hull))
        {
            string? gun = null;
            var fitted = false;
            if (comp.anchoring is { } anchored && !TerminatingOrDeleted(anchored))
            {
                gun = MetaData(anchored).EntityPrototype?.ID;
                fitted = HasComp<WFShipwrightFittedComponent>(anchored);
            }

            var xform = Transform(hardpoint);
            var tile = new Vector2i((int) MathF.Floor(xform.LocalPosition.X), (int) MathF.Floor(xform.LocalPosition.Y));
            mounts.Add(new WFShipwrightMountView(GetNetEntity(hardpoint), tile, MetaData(hardpoint).EntityPrototype?.ID ?? string.Empty,
                comp.CompatibleSizes.ToString(), (int) comp.CompatibleTypes, HasComp<WFShipwrightStockComponent>(hardpoint),
                HasComp<WFShipwrightFittedComponent>(hardpoint) || HasComp<WFShipwrightStockComponent>(hardpoint), gun, fitted));
        }

        var registered = TryComp<WFHideoutShipComponent>(hull, out var owned);
        return new WFShipwrightHullView(Name(hull), survey.Frame, registered && HasComp<WFShipwrightShipComponent>(hull),
            registered && owned!.Owner == outpost.Comp.Owner, survey.Tiles, survey.Budget, survey.Spent, mounts,
            new Dictionary<string, int>(survey.Systems), new Dictionary<string, int>(survey.Unfitted),
            survey.UnfittedWeapons.Count + survey.UnfittedMounts.Count, survey.UnfittedPoints, survey.Certified, GetNetEntity(hull),
            survey.MaxMounts, FreeEdgeTiles(hull));
    }

    /// <summary>The hull's hardpoints, in a stable order.</summary>
    public List<(EntityUid Uid, HardpointComponent Comp)> Hardpoints(EntityUid hull)
    {
        var list = new List<(EntityUid, HardpointComponent)>();
        var children = Transform(hull).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (TryComp<HardpointComponent>(child, out var hardpoint))
                list.Add((child, hardpoint));
        }

        list.Sort((a, b) =>
        {
            var pa = Transform(a.Item1).LocalPosition;
            var pb = Transform(b.Item1).LocalPosition;
            return pa.Y != pb.Y ? pb.Y.CompareTo(pa.Y) : pa.X.CompareTo(pb.X);
        });
        return list;
    }

    private float WorkScale(EntityUid hull)
    {
        var rules = Rules;
        return CompOrNull<WFShipwrightShipComponent>(hull)?.Frame != null ? rules.FrameWorkScale : rules.CustomWorkScale;
    }

    /// <summary>Shares a job's price out over its items, so an abort refunds what wasn't done.</summary>
    private static void Spread(List<WFDrydockWorkItem> items, int price)
    {
        if (items.Count == 0)
            return;

        var each = price / items.Count;
        foreach (var item in items)
        {
            item.Cost = each;
        }

        items[0].Cost += price - each * items.Count;
    }

    /// <summary>
    /// Has the cranes build a frame from its keel on the bay's empty pad, for the frame's price. The finished hull is
    /// registered as the owner's ship. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryBuildFrame(EntityUid user, Entity<WFShipwrightConsoleComponent> console, string frameId, out string reason)
    {
        if (console.Comp.Faction)
        {
            reason = "wf-shipwright-faction-no-frames";
            return false;
        }

        if (Blocked(console, out var drydock) is { } blocked)
        {
            reason = blocked;
            return false;
        }

        TryOutpost(console, out var outpost);
        TryGetBay(console, out var pad);
        var owner = outpost.Comp.Owner;
        if (!_outposts.IsOwnerOf(user, outpost.Comp))
        {
            reason = "wf-hideout-build-not-owner";
            return false;
        }

        if (pad.Comp.Guiding || pad.Comp.Parked is { } parked && !TerminatingOrDeleted(parked))
        {
            reason = "wf-hideout-ship-no-room";
            return false;
        }

        if (_hideouts.IsBusy(owner) || _hideouts.GetStored(owner) != null || _hideouts.TryGetShip(owner, out _))
        {
            reason = "wf-hideout-ship-already";
            return false;
        }

        if (!_proto.TryIndex<WFShipwrightFramePrototype>(frameId, out var frame) || Transform(outpost).MapUid is not { } map)
        {
            reason = "wf-shipwright-frame-failed";
            return false;
        }

        string yaml;
        try
        {
            yaml = _resources.ContentFileReadAllText(frame.Grid);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read shipwright frame {frame.ID} from {frame.Grid}: {e}");
            reason = "wf-shipwright-frame-failed";
            return false;
        }

        if (!_shipyard.TryAddSavedShip(yaml, out var loaded) || !TryComp<MapGridComponent>(loaded, out var grid))
        {
            reason = "wf-shipwright-frame-failed";
            return false;
        }

        if (!_pads.TryPlace(loaded.Value, grid, pad, outpost, out var placement, out _, needPower: false))
        {
            Del(loaded.Value);
            reason = "wf-shipwright-frame-too-big";
            return false;
        }

        if (!_bank.TryBankWithdraw(user, frame.Price))
        {
            Del(loaded.Value);
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        var hull = loaded.Value;
        _transform.SetCoordinates(hull, new EntityCoordinates(map, placement.Value.Position));
        _transform.SetWorldRotation(hull, placement.Value.Rotation);
        var items = StripToKeel(hull, grid, Rules.FrameWorkScale);
        _shipyard.StripForResale(hull);
        RemComp<VesselComponent>(hull);
        EnsureComp<WFShipwrightShipComponent>(hull).Frame = frame.ID;
        _metaData.SetEntityName(hull, Loc.GetString(frame.Name));
        _pads.Park(hull, pad);

        Spread(items, frame.Price);
        console.Comp.Job = WFShipwrightJob.Frame;
        console.Comp.Orderer = user;
        _drydock.StartJob(console, drydock!, hull, user, items, frame.Price);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} had the shipwright build frame {frame.ID} as {ToPrettyString(hull):grid} for {frame.Price}");
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Takes a freshly loaded frame down to one keel tile, listing the rest as crane work: its tiles, then everything that
    /// was anchored on them except guns and systems, which are fitted by refits.
    /// </summary>
    private List<WFDrydockWorkItem> StripToKeel(EntityUid hull, MapGridComponent grid, float scale)
    {
        var items = new List<WFDrydockWorkItem>();
        var parts = new List<EntityUid>();
        var children = Transform(hull).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            parts.Add(child);
        }

        foreach (var part in parts)
        {
            var xform = Transform(part);
            if (xform.Anchored && MetaData(part).EntityPrototype is { } proto
                && !HasComp<HardpointAnchorableOnlyComponent>(part) && SystemOf(part) == null)
            {
                items.Add(new WFDrydockWorkItem
                {
                    Kind = WFDrydockWorkKind.Install,
                    Prototype = proto.ID,
                    LocalPos = xform.LocalPosition,
                    Rotation = xform.LocalRotation,
                    WorkScale = scale,
                });
            }

            Del(part);
        }

        var tiles = new List<(Vector2i Index, Tile Tile)>();
        var enumerator = _maps.GetAllTilesEnumerator(hull, grid);
        while (enumerator.MoveNext(out var tile))
        {
            tiles.Add((tile.Value.GridIndices, tile.Value.Tile));
        }

        var centre = grid.LocalAABB.Center;
        var keel = tiles.MinBy(t => ((Vector2) t.Index + new Vector2(0.5f) - centre).LengthSquared());
        grid.CanSplit = false;
        var clear = new List<(Vector2i, Tile)>();
        foreach (var (index, tile) in tiles)
        {
            if (index == keel.Index)
                continue;

            clear.Add((index, Tile.Empty));
            items.Insert(0, new WFDrydockWorkItem
            {
                Kind = WFDrydockWorkKind.Tile,
                TileIndex = index,
                TileType = tile.TypeId,
                LocalPos = (Vector2) index + new Vector2(0.5f),
                WorkScale = scale,
            });
        }

        _maps.SetTiles(hull, grid, clear);
        return items;
    }

    /// <summary>
    /// Registers the hull on the bay's pad as the owner's shipwright ship: one they hold, or one nobody owns. A hull built
    /// by hand counts its own size towards its points; everything already on it waits to be certified.
    /// </summary>
    public bool TryRegister(EntityUid user, Entity<WFShipwrightConsoleComponent> console, out string reason)
    {
        if (!TryOutpost(console, out var outpost) || !TryGetBay(console, out var pad)
            || pad.Comp.Parked is not { } hull || TerminatingOrDeleted(hull))
        {
            reason = "wf-hideout-ship-none-parked";
            return false;
        }

        if (HasComp<WFHideoutShipComponent>(hull))
        {
            reason = "wf-hideout-ship-already";
            return false;
        }

        var owned = TryComp<WFShipAccessComponent>(hull, out var access) && access.OwnerUsers.Count > 0
                    || HasComp<ShipOwnershipComponent>(hull);
        if (owned && !_hideouts.Holds(user, outpost.Comp.Owner, hull))
        {
            reason = "wf-hideout-ship-not-yours";
            return false;
        }

        if (!HasComp<WFShipwrightShipComponent>(hull) && Survey(hull).Tiles > Rules.CustomMaxTiles)
        {
            reason = "wf-shipwright-hull-too-big";
            return false;
        }

        EnsureComp<WFShipwrightShipComponent>(hull);
        RemComp<VesselComponent>(hull);
        return _hideouts.RegisterHull(user, outpost, hull, out reason);
    }

    /// <summary>
    /// Has the cranes look over every gun and system on the owner's hull that the shipwright didn't fit, for the
    /// certification price each, so they count as fitted. All of it has to fit the points. <paramref name="reason"/> is a
    /// loc id when it fails.
    /// </summary>
    public bool TryCertify(EntityUid user, Entity<WFShipwrightConsoleComponent> console, out string reason)
    {
        if (!TryOwnHull(user, console, out var outpost, out var hull, out var drydock, out reason))
            return false;

        var survey = Survey(hull, outpost);
        var parts = survey.UnfittedWeapons.Concat(survey.UnfittedSystems).Concat(survey.UnfittedMounts).ToList();
        if (parts.Count == 0)
        {
            reason = "wf-shipwright-nothing-to-certify";
            return false;
        }

        if (survey.Spent + survey.UnfittedPoints > survey.Budget || survey.OverLimit.Count > 0)
        {
            reason = survey.OverLimit.Count > 0 ? "wf-shipwright-over-limit" : "wf-shipwright-over-budget";
            return false;
        }

        var price = parts.Count * Rules.CertifyPrice;
        if (!_bank.TryBankWithdraw(user, price))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        var scale = WorkScale(hull);
        var items = parts.Select(part => new WFDrydockWorkItem
        {
            Kind = WFDrydockWorkKind.Inspect,
            Target = part.Uid,
            LocalPos = PositionOn(hull, part.Uid),
            WorkScale = scale,
        }).ToList();
        Spread(items, price);
        console.Comp.Job = WFShipwrightJob.Certify;
        console.Comp.Orderer = user;
        _drydock.StartJob(console, drydock, hull, user, items, price);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"{ToPrettyString(user):user} had {parts.Count} parts of {ToPrettyString(hull):grid} certified for {price}");
        return true;
    }

    /// <summary>
    /// Has the cranes refit the owner's hull: each listed hardpoint takes the listed gun (an empty id clears it) and each
    /// listed system goes to the listed count. The result has to fit the hull's points and the systems' limits.
    /// <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryRefit(EntityUid user, Entity<WFShipwrightConsoleComponent> console, Dictionary<NetEntity, string> mounts,
        Dictionary<string, int> systems, out string reason)
    {
        return TryRefit(user, console, mounts, systems, new Dictionary<NetEntity, string>(), new List<WFShipwrightPlacement>(), out reason);
    }

    /// <summary>
    /// As the other overload, and also swaps listed hardpoints for another type (an empty id takes the hardpoint off, with
    /// its gun) and lays new hardpoints, with their guns, on free edge tiles.
    /// </summary>
    public bool TryRefit(EntityUid user, Entity<WFShipwrightConsoleComponent> console, Dictionary<NetEntity, string> mounts,
        Dictionary<string, int> systems, Dictionary<NetEntity, string> hardpoints, List<WFShipwrightPlacement> placements,
        out string reason)
    {
        if (!TryOwnHull(user, console, out var outpost, out var hull, out var drydock, out reason))
            return false;

        var survey = Survey(hull, outpost);
        var rules = Rules;
        var scale = WorkScale(hull);
        var guns = Guns().ToDictionary(gun => gun.Id);
        var items = new List<WFDrydockWorkItem>();
        var spent = survey.Spent;
        var price = 0;
        var mountCount = survey.Mounts;
        var mountTypes = MountTypes().ToDictionary(type => type.Id);

        // Hardpoints swapped or taken off: the old one comes off with its gun, and a new one goes on in its place.
        var swapped = new Dictionary<NetEntity, WFShipwrightMountTypeView?>();
        foreach (var (net, mountId) in hardpoints)
        {
            if (!TryGetEntity(net, out var hardpointUid) || Transform(hardpointUid.Value).ParentUid != hull
                || !TryComp<HardpointComponent>(hardpointUid, out var hardpoint))
            {
                reason = "wf-shipwright-refit-invalid";
                return false;
            }

            WFShipwrightMountTypeView? type = null;
            if (mountId.Length > 0 && !mountTypes.TryGetValue(mountId, out type))
            {
                reason = "wf-shipwright-refit-invalid";
                return false;
            }

            var xform = Transform(hardpointUid.Value);
            if (hardpoint.anchoring is { } gunOn && !TerminatingOrDeleted(gunOn))
            {
                if (HasComp<WFShipwrightFittedComponent>(gunOn))
                    spent -= PointsOf(gunOn);
                items.Add(new WFDrydockWorkItem { Kind = WFDrydockWorkKind.Remove, Target = gunOn, LocalPos = xform.LocalPosition, WorkScale = scale });
                price += rules.Labour;
            }

            if (HasComp<WFShipwrightFittedComponent>(hardpointUid) || HasComp<WFShipwrightStockComponent>(hardpointUid))
                spent -= MountPoints(hardpointUid.Value, hardpoint);
            items.Add(new WFDrydockWorkItem { Kind = WFDrydockWorkKind.Remove, Target = hardpointUid.Value, LocalPos = xform.LocalPosition, WorkScale = scale });
            price += rules.Labour;
            mountCount--;
            swapped[net] = type;

            if (type == null)
                continue;

            mountCount++;
            spent += type.Points;
            price += type.Price;
            items.Add(new WFDrydockWorkItem
            {
                Kind = WFDrydockWorkKind.Install,
                Prototype = type.Id,
                LocalPos = xform.LocalPosition,
                Rotation = xform.LocalRotation,
                WorkScale = scale,
            });
        }

        foreach (var (net, gunId) in mounts)
        {
            if (!TryGetEntity(net, out var hardpointUid) || Transform(hardpointUid.Value).ParentUid != hull
                || !TryComp<HardpointComponent>(hardpointUid, out var hardpoint))
            {
                reason = "wf-shipwright-refit-invalid";
                return false;
            }

            var xform = Transform(hardpointUid.Value);
            var size = (int) hardpoint.CompatibleSizes;
            var types = (int) hardpoint.CompatibleTypes;
            if (swapped.TryGetValue(net, out var replacement))
            {
                // The swap already took the old gun off; a hardpoint taken off takes no gun.
                if (replacement == null)
                    continue;

                size = replacement.Size;
                types = replacement.Types;
            }
            else
            {
                var current = hardpoint.anchoring is { } anchored && !TerminatingOrDeleted(anchored) ? anchored : (EntityUid?) null;
                var currentId = current is { } cur ? MetaData(cur).EntityPrototype?.ID : null;
                if (gunId == (currentId ?? string.Empty) && (current == null || HasComp<WFShipwrightFittedComponent>(current)))
                    continue;

                if (current is { } old)
                {
                    if (HasComp<WFShipwrightFittedComponent>(old))
                        spent -= PointsOf(old);
                    items.Add(new WFDrydockWorkItem { Kind = WFDrydockWorkKind.Remove, Target = old, LocalPos = xform.LocalPosition, WorkScale = scale });
                    price += rules.Labour;
                }
            }

            if (gunId.Length == 0)
                continue;

            if (!TryAddGun(items, guns, gunId, size, types, xform.LocalPosition, xform.LocalRotation, scale, ref spent, ref price))
            {
                reason = "wf-shipwright-refit-wont-fit";
                return false;
            }
        }

        // New hardpoints on free edge tiles, each with its gun if one is picked.
        var free = FreeEdgeTiles(hull).ToHashSet();
        var laid = new HashSet<Vector2i>();
        foreach (var placement in placements)
        {
            if (!free.Contains(placement.Tile) || !laid.Add(placement.Tile) || !mountTypes.TryGetValue(placement.Hardpoint, out var type))
            {
                reason = "wf-shipwright-refit-bad-tile";
                return false;
            }

            var at = (Vector2) placement.Tile + new Vector2(0.5f);
            mountCount++;
            spent += type.Points;
            price += type.Price;
            items.Add(new WFDrydockWorkItem { Kind = WFDrydockWorkKind.Install, Prototype = type.Id, LocalPos = at, WorkScale = scale });
            if (placement.Gun.Length > 0
                && !TryAddGun(items, guns, placement.Gun, type.Size, type.Types, at, Angle.Zero, scale, ref spent, ref price))
            {
                reason = "wf-shipwright-refit-wont-fit";
                return false;
            }
        }

        if (mountCount > survey.MaxMounts)
        {
            reason = "wf-shipwright-too-many-mounts";
            return false;
        }

        var reserved = new HashSet<Vector2i>();
        foreach (var (systemId, count) in systems)
        {
            if (!_proto.TryIndex<WFShipwrightSystemPrototype>(systemId, out var system) || count < 0)
            {
                reason = "wf-shipwright-refit-invalid";
                return false;
            }

            if (IsMounted(system))
            {
                reason = "wf-shipwright-refit-invalid";
                return false;
            }

            if (system.Limit > 0 && count + survey.Unfitted.GetValueOrDefault(systemId) > system.Limit)
            {
                reason = "wf-shipwright-over-limit";
                return false;
            }

            var fitted = FittedOf(hull, system);
            for (var i = count; i < fitted.Count; i++)
            {
                spent -= system.Points;
                price += rules.Labour;
                items.Add(new WFDrydockWorkItem
                {
                    Kind = WFDrydockWorkKind.Remove,
                    Target = fitted[i],
                    LocalPos = PositionOn(hull, fitted[i]),
                    WorkScale = scale,
                });
            }

            for (var i = fitted.Count; i < count; i++)
            {
                if (!TryFindFreeTile(hull, reserved, out var tile))
                {
                    reason = "wf-shipwright-refit-no-room";
                    return false;
                }

                reserved.Add(tile);
                spent += system.Points;
                price += system.Price;
                items.Add(new WFDrydockWorkItem
                {
                    Kind = WFDrydockWorkKind.Install,
                    Prototype = system.Install,
                    LocalPos = (Vector2) tile + new Vector2(0.5f),
                    WorkScale = scale,
                });
            }
        }

        if (items.Count == 0)
        {
            reason = "wf-shipwright-refit-nothing";
            return false;
        }

        if (spent > survey.Budget)
        {
            reason = "wf-shipwright-over-budget";
            return false;
        }

        if (!_bank.TryBankWithdraw(user, price))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        Spread(items, price);
        console.Comp.Job = WFShipwrightJob.Refit;
        console.Comp.Orderer = user;
        _drydock.StartJob(console, drydock, hull, user, items, price);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"{ToPrettyString(user):user} ordered a {items.Count}-item refit of {ToPrettyString(hull):grid} for {price}");
        return true;
    }

    /// <summary>Queues a gun onto a hardpoint of the given size and types, if it fits.</summary>
    private bool TryAddGun(List<WFDrydockWorkItem> items, Dictionary<string, WFShipwrightGunView> guns, string gunId, int size,
        int types, Vector2 at, Angle rotation, float scale, ref int spent, ref int price)
    {
        if (!guns.TryGetValue(gunId, out var gun) || (types & gun.Types) == 0 || size < gun.Size)
            return false;

        spent += gun.Points;
        price += gun.Price;
        items.Add(new WFDrydockWorkItem
        {
            Kind = WFDrydockWorkKind.Install,
            Prototype = gunId,
            LocalPos = at,
            Rotation = rotation,
            Mounted = true,
            WorkScale = scale,
        });
        return true;
    }

    /// <summary>
    /// Floor tiles on the hull's edge (open space beside them, diagonals included) with no hardpoint and nothing solid on
    /// them: where a new hardpoint may go.
    /// </summary>
    public List<Vector2i> FreeEdgeTiles(EntityUid hull)
    {
        var result = new List<Vector2i>();
        if (!TryComp<MapGridComponent>(hull, out var grid))
            return result;

        var tiles = _maps.GetAllTilesEnumerator(hull, grid);
        while (tiles.MoveNext(out var tile))
        {
            var index = tile.Value.GridIndices;
            var edge = false;
            for (var dx = -1; dx <= 1 && !edge; dx++)
            for (var dy = -1; dy <= 1 && !edge; dy++)
            {
                if ((dx != 0 || dy != 0) && (!_maps.TryGetTileRef(hull, grid, index + new Vector2i(dx, dy), out var near) || near.Tile.IsEmpty))
                    edge = true;
            }

            if (!edge || IsOccupied(hull, grid, index) || HasHardpoint(hull, grid, index))
                continue;

            result.Add(index);
        }

        return result;
    }

    private bool HasHardpoint(EntityUid hull, MapGridComponent grid, Vector2i index)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(hull, grid, index);
        while (anchored.MoveNext(out var ent))
        {
            if (HasComp<HardpointComponent>(ent))
                return true;
        }

        return false;
    }

    /// <summary>The owner's registered shipwright hull on the bay, with the console free to work it.</summary>
    private bool TryOwnHull(EntityUid user, Entity<WFShipwrightConsoleComponent> console, out Entity<WFOutpostComponent> outpost,
        out EntityUid hull, out WFDrydockConsoleComponent drydock, out string reason)
    {
        hull = default;
        drydock = default!;
        outpost = default;
        if (Blocked(console, out var dock) is { } blocked)
        {
            reason = blocked;
            return false;
        }

        TryOutpost(console, out outpost);
        TryGetBay(console, out var pad);
        drydock = dock!;
        if (!_outposts.IsOwnerOf(user, outpost.Comp))
        {
            reason = "wf-hideout-build-not-owner";
            return false;
        }

        if (pad.Comp.Parked is not { } parked || TerminatingOrDeleted(parked) || !HasComp<WFShipwrightShipComponent>(parked)
            || !TryComp<WFHideoutShipComponent>(parked, out var owned) || owned.Owner != outpost.Comp.Owner)
        {
            reason = "wf-shipwright-not-registered";
            return false;
        }

        hull = parked;
        reason = string.Empty;
        return true;
    }

    /// <summary>Whether a system mounts on a hardpoint, so it is fitted from the mounts.</summary>
    public bool IsMounted(WFShipwrightSystemPrototype system)
    {
        return _proto.TryIndex(system.Install, out var proto)
               && proto.Components.ContainsKey(_factory.GetComponentName<HardpointAnchorableOnlyComponent>());
    }

    /// <summary>Refit points a fitted part takes: its system's, or its gun size's.</summary>
    private int PointsOf(EntityUid part)
    {
        if (SystemOf(part) is { } system)
            return system.Points;

        return TryComp<HardpointAnchorableOnlyComponent>(part, out var gun) ? WeaponPoints(gun.CompatibleSizes) : 0;
    }

    /// <summary>Fitted entities of a system on the hull, or every one of them with <paramref name="any"/>.</summary>
    private List<EntityUid> FittedOf(EntityUid hull, WFShipwrightSystemPrototype system, bool any = false)
    {
        var list = new List<EntityUid>();
        var children = Transform(hull).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if ((any || HasComp<WFShipwrightFittedComponent>(child)) && SystemOf(child) == system)
                list.Add(child);
        }

        return list;
    }

    /// <summary>Where on the hull an entity is, through whatever holds it.</summary>
    private Vector2 PositionOn(EntityUid hull, EntityUid uid)
    {
        var xform = Transform(uid);
        while (xform.ParentUid != hull && xform.ParentUid.IsValid())
        {
            uid = xform.ParentUid;
            xform = Transform(uid);
        }

        return xform.LocalPosition;
    }

    /// <summary>A floor tile with nothing solid anchored on it, nearest the hull's middle.</summary>
    private bool TryFindFreeTile(EntityUid hull, HashSet<Vector2i> reserved, out Vector2i found)
    {
        found = default;
        if (!TryComp<MapGridComponent>(hull, out var grid))
            return false;

        var centre = grid.LocalAABB.Center;
        var best = float.MaxValue;
        var tiles = _maps.GetAllTilesEnumerator(hull, grid);
        while (tiles.MoveNext(out var tile))
        {
            var index = tile.Value.GridIndices;
            if (reserved.Contains(index))
                continue;

            var distance = ((Vector2) index + new Vector2(0.5f) - centre).LengthSquared();
            if (distance >= best || IsOccupied(hull, grid, index))
                continue;

            best = distance;
            found = index;
        }

        return best < float.MaxValue;
    }

    private bool IsOccupied(EntityUid hull, MapGridComponent grid, Vector2i index)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(hull, grid, index);
        while (anchored.MoveNext(out var ent))
        {
            if (TryComp<PhysicsComponent>(ent, out var body) && body.CanCollide && body.Hard || SystemOf(ent.Value) != null)
                return true;
        }

        return false;
    }

    private void OnPartInstalled(Entity<WFShipwrightShipComponent> hull, ref WFDrydockPartInstalledEvent args)
    {
        if (HasComp<HardpointComponent>(args.Part))
        {
            EnsureComp<WFShipwrightFittedComponent>(args.Part);
            // A frame's own hardpoints come with it, free of points.
            if (TryComp<WFShipwrightConsoleComponent>(args.Console, out var console) && console.Job == WFShipwrightJob.Frame)
                EnsureComp<WFShipwrightStockComponent>(args.Part);
            return;
        }

        if (HasComp<HardpointAnchorableOnlyComponent>(args.Part) || SystemOf(args.Part) != null)
            EnsureComp<WFShipwrightFittedComponent>(args.Part);
    }

    private void OnPartInspected(Entity<WFShipwrightShipComponent> hull, ref WFDrydockPartInspectedEvent args)
    {
        if (HasComp<WFShipwrightConsoleComponent>(args.Console))
            EnsureComp<WFShipwrightFittedComponent>(args.Part);
    }

    private void OnJobEnded(Entity<WFShipwrightConsoleComponent> console, ref WFDrydockJobEndedEvent args)
    {
        var job = console.Comp.Job;
        var orderer = console.Comp.Orderer;
        console.Comp.Job = WFShipwrightJob.None;
        console.Comp.Orderer = null;
        if (TerminatingOrDeleted(args.Ship) || !TryOutpost(console, out var outpost))
            return;

        if (job == WFShipwrightJob.Frame && TryComp<MapGridComponent>(args.Ship, out var grid))
        {
            grid.CanSplit = true;
            if (args.Finished && orderer is { } user && !TerminatingOrDeleted(user))
            {
                if (!_hideouts.RegisterHull(user, outpost, args.Ship, out var reason))
                    _popup.PopupEntity(Loc.GetString(reason), console, user);
            }
        }
        else if (args.Finished && TryComp<WFHideoutShipComponent>(args.Ship, out var owned) && owned.Owner == outpost.Comp.Owner)
        {
            if (!_hideouts.TrySaveHome(owned.Owner, out var reason) && orderer is { } user && !TerminatingOrDeleted(user))
                _popup.PopupEntity(Loc.GetString("wf-hideout-ship-not-saved", ("reason", Loc.GetString(reason))), console, user);
        }

        UpdateUi(console);
    }

    private void OnBuildFrameMessage(Entity<WFShipwrightConsoleComponent> console, ref WFShipwrightBuildFrameMessage args)
    {
        Report(console, args.Actor, TryBuildFrame(args.Actor, console, args.Frame, out var reason), reason);
    }

    private void OnRegisterMessage(Entity<WFShipwrightConsoleComponent> console, ref WFShipwrightRegisterMessage args)
    {
        if (console.Comp.Faction)
        {
            Report(console, args.Actor, false, "wf-shipwright-faction-register");
            return;
        }

        Report(console, args.Actor, TryRegister(args.Actor, console, out var reason), reason);
    }

    private void OnCertifyMessage(Entity<WFShipwrightConsoleComponent> console, ref WFShipwrightCertifyMessage args)
    {
        if (console.Comp.Faction)
            return;

        Report(console, args.Actor, TryCertify(args.Actor, console, out var reason), reason);
    }

    private void OnRefitMessage(Entity<WFShipwrightConsoleComponent> console, ref WFShipwrightRefitMessage args)
    {
        if (console.Comp.Faction)
        {
            Report(console, args.Actor, TryFactionRefit(args.Actor, console, args.Mounts, args.Systems, out var why), why);
            return;
        }

        Report(console, args.Actor, TryRefit(args.Actor, console, args.Mounts, args.Systems, args.Hardpoints, args.Placements, out var reason), reason);
    }

    private void Report(Entity<WFShipwrightConsoleComponent> console, EntityUid user, bool ok, string reason)
    {
        if (!ok && reason.Length > 0)
            _popup.PopupEntity(Loc.GetString(reason), console, user);
        UpdateUi(console);
    }
}
