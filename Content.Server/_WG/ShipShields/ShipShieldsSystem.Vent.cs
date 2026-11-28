using System.Numerics;
using Content.Server._Mono.Detection;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WG.ShipShields;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.FireControl;
using Content.Shared._WG.ShipShields;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Crescent.ShipShields;

// A vent trades the ship's field and guns for fast cooling, and runs until no heat is left.
public sealed partial class ShipShieldsSystem
{
    /// <summary>Share of capacity in hard heat at which an NPC crew vents under a raised field once the shooting stops.</summary>
    public const float NpcVentHeat = 0.5f;

    /// <summary>Share of capacity an NPC crew vents at with no field to lose or nothing to fight.</summary>
    public const float NpcIdleVentHeat = 0.15f;

    /// <summary>Share of capacity at which an NPC crew stops firing under a raised field, to leave the field room for hits.</summary>
    public const float NpcHoldFireHeat = 0.7f;

    /// <summary>Share of capacity an NPC crew vents at once an overload ends, before it fights on.</summary>
    public const float NpcOverloadVentHeat = 0.5f;

    /// <summary>How long the field must go unhit before an NPC crew drops it to vent.</summary>
    public static readonly TimeSpan NpcVentQuiet = TimeSpan.FromSeconds(6);

    /// <summary>How long an NPC crew waits after its last hit to vent with the field already down.</summary>
    public static readonly TimeSpan NpcDownedVentQuiet = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan PlumeSpacing = TimeSpan.FromSeconds(0.3);
    private const int MaxPlumes = 10;

    /// <summary>How far past its tile's centre a plume sits, so its two-tile sprite starts at the hull's edge.</summary>
    private const float PlumeReach = 1.5f;

    private static readonly (Vector2i Offset, Angle Facing)[] PlumeSides =
    {
        (new Vector2i(0, -1), Angle.Zero), (new Vector2i(1, 0), Angle.FromDegrees(90)),
        (new Vector2i(0, 1), Angle.FromDegrees(180)), (new Vector2i(-1, 0), Angle.FromDegrees(270)),
    };

    private readonly HashSet<EntityUid> _wgVenting = new();
    private readonly List<EntityUid> _wgVentGone = new();

    /// <summary>Ships an NPC is flying, and whether it has a target.</summary>
    private readonly Dictionary<EntityUid, bool> _wgNpcHulls = new();

    private void InitializeWolfgateVents()
    {
        SubscribeLocalEvent<WGShipHeatComponent, GetThermalSignatureEvent>(OnWolfgateHullSignature);
    }

    /// <summary>Whether a ship is venting: its field stays down and its guns stay silent until no heat is left.</summary>
    public bool IsWolfgateShipVenting(EntityUid grid)
    {
        return _wgVenting.Contains(grid) && TryComp<WGShipHeatComponent>(grid, out var hull) && hull.Venting;
    }

    /// <summary>
    /// Whether a ship's crew can start a vent: it holds heat, isn't venting, no outpost generator rules its hull and
    /// its generator isn't waiting out an overload.
    /// </summary>
    public bool CanWolfgateVent(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || !HasComp<MapGridComponent>(grid))
            return false;
        var generator = SyncWolfgateShipHeat(grid);
        return TryComp<WGShipHeatComponent>(grid, out var hull) && !hull.Venting && hull.Heat > 0f &&
            (generator is not { } uid || HasComp<WGShipShieldCostComponent>(uid) && !IsWolfgateOverloaded(uid));
    }

    /// <summary>Whether a generator is waiting out an overload, which ends once below a second; as in Starsector, an overloaded ship can't vent.</summary>
    public bool IsWolfgateOverloaded(EntityUid generator)
    {
        return TryComp<ShipShieldEmitterComponent>(generator, out var emitter) && emitter.OverloadAccumulator >= 1f;
    }

    /// <summary>
    /// Starts venting a ship's heat. The field drops, the guns fall silent and the hull cools at its fast rate; none of
    /// it can be called off before the heat is gone.
    /// </summary>
    public bool StartWolfgateVent(EntityUid grid)
    {
        if (!CanWolfgateVent(grid))
            return false;
        var hull = Comp<WGShipHeatComponent>(grid);
        hull.Venting = true;
        hull.VentHold = WGShipShieldEffects.MinVentSeconds;
        hull.NextPlume = _wgShieldTiming.CurTime;
        _wgVenting.Add(grid);
        // Clients draw the vent on radar from this.
        EnsureComp<WGShipVentingComponent>(grid);
        LowerWolfgateShield(grid, "vent");
        FindWolfgatePlumes(grid, hull);
        PlayWolfgateHullSound(grid, hull.VentSound);
        PopupWolfgateShipConsoles(grid, Loc.GetString("wg-shield-vent-popup"), PopupType.MediumCaution);
        return true;
    }

    private void StopWolfgateVent(EntityUid grid, WGShipHeatComponent hull)
    {
        hull.Venting = false;
        hull.Plumes.Clear();
        _wgVenting.Remove(grid);
        RemComp<WGShipVentingComponent>(grid);
    }

    /// <summary>Whole seconds a venting ship still needs; 0 when it isn't venting.</summary>
    public int GetWolfgateVentSeconds(EntityUid grid)
    {
        if (!IsWolfgateShipVenting(grid))
            return 0;
        var hull = Comp<WGShipHeatComponent>(grid);
        var rate = WolfgateCoolingRate(grid, hull);
        if (!(rate > 0f) || !float.IsFinite(rate))
            return -1;
        return Math.Max(1, (int) MathF.Ceiling(MathF.Max(hull.Heat / rate, hull.VentHold)));
    }

    /// <summary>A hot hull glows on long-range sensors by the heat it holds, and a venting one flares on top of that.</summary>
    private void OnWolfgateHullSignature(Entity<WGShipHeatComponent> grid, ref GetThermalSignatureEvent args)
    {
        if (float.IsFinite(grid.Comp.Heat) && grid.Comp.Heat > 0f)
            args.Signature += grid.Comp.Heat * MathF.Max(0f, grid.Comp.HeatSignature);
        if (grid.Comp.Venting)
            args.Signature += MathF.Max(0f, grid.Comp.VentSignature);
    }

    /// <summary>Tells whoever is at a ship's helm and gunnery consoles.</summary>
    private void PopupWolfgateShipConsoles(EntityUid grid, string message, PopupType type)
    {
        var consoles = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.GridUid != grid)
                continue;
            foreach (var actor in _wgShieldUi.GetActors(uid, FireControlConsoleUiKey.Key))
                _wgShieldPopup.PopupEntity(message, uid, actor, type);
        }

        // Pilots fire from the helm in the cockpit view, so they are told too.
        var helms = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (helms.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.GridUid != grid)
                continue;
            foreach (var actor in _wgShieldUi.GetActors(uid, ShuttleConsoleUiKey.Key))
                _wgShieldPopup.PopupEntity(message, uid, actor, type);
        }
    }

    /// <summary>Finds every open side of the hull's edge tiles, where a plume can leave it.</summary>
    private void FindWolfgatePlumes(EntityUid grid, WGShipHeatComponent hull)
    {
        hull.Plumes.Clear();
        if (hull.VentEffect is null || !TryComp<MapGridComponent>(grid, out var map))
            return;
        var tiles = _wgShieldMap.GetAllTilesEnumerator(grid, map);
        while (tiles.MoveNext(out var tile))
        {
            var centre = (Vector2) tile.Value.GridIndices * map.TileSize + new Vector2(map.TileSize / 2f);
            foreach (var (offset, facing) in PlumeSides)
            {
                if (_wgShieldMap.GetTileRef(grid, map, tile.Value.GridIndices + offset).Tile.IsEmpty)
                    hull.Plumes.Add((centre + (Vector2) offset * (map.TileSize * PlumeReach), facing));
            }
        }
    }

    /// <summary>Sends plumes of heat off the edge of every venting hull.</summary>
    private void UpdateWolfgateVents()
    {
        if (_wgVenting.Count == 0)
            return;
        var now = _wgShieldTiming.CurTime;
        foreach (var grid in _wgVenting)
        {
            if (!TryComp<WGShipHeatComponent>(grid, out var hull) || !hull.Venting)
            {
                _wgVentGone.Add(grid);
                continue;
            }
            if (now < hull.NextPlume || hull.VentEffect is not { } effect || hull.Plumes.Count == 0)
                continue;
            hull.NextPlume = now + PlumeSpacing;
            for (var i = Math.Clamp(hull.Plumes.Count / 8, 2, MaxPlumes); i > 0; i--)
            {
                var (position, facing) = _wgShieldRandom.Pick(hull.Plumes);
                // Slid along the hull's edge so the plumes don't line up with the tiles.
                var slide = facing.RotateVec(new Vector2(_wgShieldRandom.NextFloat(-0.4f, 0.4f), 0f));
                SpawnAttachedTo(effect, new EntityCoordinates(grid, position + slide), rotation: facing);
            }
        }
        foreach (var grid in _wgVentGone)
            _wgVenting.Remove(grid);
        _wgVentGone.Clear();
    }

    /// <summary>Has every ship an NPC is flying vent when its crew would: hot and left alone, or with nothing to fight.</summary>
    private void UpdateWolfgateNpcVents()
    {
        _wgNpcHulls.Clear();
        var steering = EntityQueryEnumerator<ShipSteererComponent, TransformComponent>();
        while (steering.MoveNext(out _, out _, out var transform))
        {
            if (transform.GridUid is { } grid)
                _wgNpcHulls.TryAdd(grid, false);
        }
        var targeting = EntityQueryEnumerator<ShipTargetingComponent, TransformComponent>();
        while (targeting.MoveNext(out _, out _, out var transform))
        {
            if (transform.GridUid is { } grid)
                _wgNpcHulls[grid] = true;
        }
        if (_wgNpcHulls.Count == 0)
            return;
        // A player at the helm makes the call themselves.
        var pilots = EntityQueryEnumerator<PilotComponent, ActorComponent>();
        while (pilots.MoveNext(out _, out var pilot, out _))
        {
            if (pilot.Console is { } console && !TerminatingOrDeleted(console) && Transform(console).GridUid is { } flown)
                _wgNpcHulls.Remove(flown);
        }
        foreach (var (grid, fighting) in _wgNpcHulls)
        {
            if (WantsWolfgateNpcVent(grid, fighting))
                StartWolfgateVent(grid);
        }
    }

    /// <summary>
    /// Whether an NPC crew would vent this ship now. The first time it is asked after an overload ends it makes its
    /// call on that collapse.
    /// </summary>
    public bool WantsWolfgateNpcVent(EntityUid grid, bool fighting)
    {
        if (!CanWolfgateVent(grid))
            return false;
        var hull = Comp<WGShipHeatComponent>(grid);
        var recovered = hull.Collapsed;
        hull.Collapsed = false;
        var (heat, capacity) = GetWolfgateShipHeat(grid);
        if (capacity <= 0f)
            return false;
        if (!fighting)
            return heat >= capacity * NpcIdleVentHeat;
        // A bare hull has no field to win back, so its crew fights on until the guns hold.
        if (SyncWolfgateShipHeat(grid) == null)
            return IsWolfgateShipHeatHeld(grid);
        // A field back from an overload is close to full and a hit or two from the next one, so the crew clears it first.
        if (recovered && heat >= capacity * NpcOverloadVentHeat)
            return true;
        var quiet = _wgShieldTiming.CurTime - hull.LastHit;
        // With the field up only hard heat is worth dropping it for; soft heat goes by itself. Hard heat past the mark
        // where the crew holds fire can't be waited out under fire, so it vents without waiting for quiet.
        if (TryComp<ShipShieldedComponent>(grid, out var shielded) && IsWolfgateShieldLive(shielded.Shield))
            return hull.Hard >= capacity * NpcHoldFireHeat || hull.Hard >= capacity * NpcVentHeat && quiet >= NpcVentQuiet;
        return heat >= capacity * NpcIdleVentHeat && quiet >= NpcDownedVentQuiet;
    }

    /// <summary>Whether an NPC crew holds its fire: its raised field is hot enough that its own guns would leave it no room for hits.</summary>
    public bool WantsWolfgateNpcHoldFire(EntityUid grid)
    {
        if (GetWolfgateRaisedCost(grid) == null)
            return false;
        var (heat, capacity) = GetWolfgateShipHeat(grid);
        return capacity > 0f && heat >= capacity * NpcHoldFireHeat;
    }
}
