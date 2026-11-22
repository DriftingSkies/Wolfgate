using System.Diagnostics.CodeAnalysis;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Toggleable;
using Content.Shared.Tools.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WG.Mines;

/// <summary>Setting, arming and disarming <see cref="WGArmableMineComponent"/> mines.</summary>
public sealed partial class WGArmableMineSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    private readonly HashSet<EntityUid> _onTile = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WGArmableMineComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGArmableMineComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WGArmableMineComponent, WGMineSetDoAfterEvent>(OnSetDoAfter);
        SubscribeLocalEvent<WGArmableMineComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WGArmableMineComponent, WGMineDisarmDoAfterEvent>(OnDisarmDoAfter);
        SubscribeLocalEvent<WGArmableMineComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<WGArmableMineComponent, StepTriggerAttemptEvent>(OnStepTriggerAttempt);
        SubscribeLocalEvent<WGArmableMineComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGArmableMineComponent>();
        while (query.MoveNext(out var uid, out var mine))
        {
            if (mine.State != WGMineState.Arming || now < mine.ArmAt)
                continue;

            SetState((uid, mine), WGMineState.Armed);
            _audio.PlayPvs(mine.ArmSound, uid);
        }
    }

    /// <summary>Whether a mine can be set on the tile at <paramref name="coords"/>, with the refusal's locale key.</summary>
    public bool CanSet(EntityCoordinates coords, [NotNullWhen(false)] out string? reason)
    {
        return TryGetSetTile(coords, out _, out reason);
    }

    private bool TryGetSetTile(EntityCoordinates coords, out TileRef tile, [NotNullWhen(false)] out string? reason)
    {
        tile = default;
        reason = "wg-mine-set-blocked";
        if (!_turf.TryGetTileRef(coords, out var tileRef)
            || _turf.IsSpace(tileRef.Value)
            || _turf.IsTileBlocked(tileRef.Value, CollisionGroup.MobMask)
            || !TryComp<MapGridComponent>(tileRef.Value.GridUid, out var grid))
            return false;

        tile = tileRef.Value;
        var anchored = _map.GetAnchoredEntitiesEnumerator(tile.GridUid, grid, tile.GridIndices);
        while (anchored.MoveNext(out var other))
        {
            if (!HasComp<WGArmableMineComponent>(other))
                continue;

            reason = "wg-mine-set-occupied";
            return false;
        }

        // Nobody may stand on it, so no one is left on a mine as it arms.
        _onTile.Clear();
        _lookup.GetEntitiesInTile(tile, _onTile, LookupFlags.Dynamic);
        foreach (var other in _onTile)
        {
            if (!HasComp<MobStateComponent>(other))
                continue;

            reason = "wg-mine-set-someone";
            return false;
        }

        reason = null;
        return true;
    }

    private void OnMapInit(Entity<WGArmableMineComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.State != WGMineState.Safe)
            _appearance.SetData(ent, ToggleableVisuals.Enabled, ent.Comp.State == WGMineState.Armed);
    }

    private void OnAfterInteract(Entity<WGArmableMineComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || ent.Comp.State != WGMineState.Safe)
            return;

        args.Handled = true;
        if (!CanSet(args.ClickLocation, out var reason))
        {
            _popup.PopupClient(Loc.GetString(reason), args.User, args.User, PopupType.SmallCaution);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager,
            args.User,
            ent.Comp.SetDelay,
            new WGMineSetDoAfterEvent(GetNetCoordinates(args.ClickLocation)),
            ent,
            used: ent)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnHandChange = true,
            BlockDuplicate = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupClient(Loc.GetString("wg-mine-set-start", ("mine", ent)), args.User, args.User);
    }

    private void OnSetDoAfter(Entity<WGArmableMineComponent> ent, ref WGMineSetDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (_net.IsClient || ent.Comp.State != WGMineState.Safe || !_hands.IsHolding(args.User, ent.Owner))
            return;

        if (!TryGetSetTile(GetCoordinates(args.Coordinates), out var tile, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User, PopupType.SmallCaution);
            return;
        }

        if (!_hands.TryDrop(args.User, ent.Owner)
            || !TryComp<MapGridComponent>(tile.GridUid, out var grid)
            || !_transform.AnchorEntity((ent.Owner, Transform(ent)), (tile.GridUid, grid), tile.GridIndices))
            return;

        ent.Comp.ArmAt = _timing.CurTime + ent.Comp.ArmDelay;
        SetState(ent, WGMineState.Arming);
        _popup.PopupEntity(Loc.GetString("wg-mine-set-done", ("mine", ent), ("seconds", (int) ent.Comp.ArmDelay.TotalSeconds)),
            args.User,
            args.User);
    }

    private void OnInteractUsing(Entity<WGArmableMineComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.State == WGMineState.Safe || !_tool.HasQuality(args.Used, ent.Comp.DisarmQuality))
            return;

        args.Handled = true;
        if (_tool.UseTool(args.Used, args.User, ent, ent.Comp.DisarmDelay, ent.Comp.DisarmQuality, new WGMineDisarmDoAfterEvent()))
            _popup.PopupClient(Loc.GetString("wg-mine-disarm-start", ("mine", ent)), args.User, args.User);
    }

    private void OnDisarmDoAfter(Entity<WGArmableMineComponent> ent, ref WGMineDisarmDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (_net.IsClient || ent.Comp.State == WGMineState.Safe)
            return;

        // Safe first, so OnAnchorChanged leaves it alone.
        SetState(ent, WGMineState.Safe);
        _transform.Unanchor(ent);
        _popup.PopupEntity(Loc.GetString("wg-mine-disarmed", ("mine", ent)), args.User, args.User);
    }

    private void OnAnchorChanged(Entity<WGArmableMineComponent> ent, ref AnchorStateChangedEvent args)
    {
        // A set mine knocked loose, such as by prying up its floor, is a dud until set again.
        if (_net.IsClient || args.Anchored || args.Detaching || ent.Comp.State == WGMineState.Safe || TerminatingOrDeleted(ent))
            return;

        SetState(ent, WGMineState.Safe);
    }

    private void OnStepTriggerAttempt(Entity<WGArmableMineComponent> ent, ref StepTriggerAttemptEvent args)
    {
        if (ent.Comp.State == WGMineState.Armed)
            args.Continue = true;
    }

    private void OnExamined(Entity<WGArmableMineComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.State switch
        {
            WGMineState.Safe => "wg-mine-examine-safe",
            WGMineState.Arming => "wg-mine-examine-arming",
            _ => "wg-mine-examine-armed",
        }));
    }

    private void SetState(Entity<WGArmableMineComponent> ent, WGMineState state)
    {
        ent.Comp.State = state;
        Dirty(ent);
        _appearance.SetData(ent, ToggleableVisuals.Enabled, state == WGMineState.Armed);
    }
}
