using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    private readonly HashSet<EntityUid> _wgMovingShunts = new();
    private float _wgShuntAccumulator;

    /// <summary>Sets a requested allocation without immediately changing protection.</summary>
    public bool RequestWolfgateShieldShunt(EntityUid grid, float directionRadians, float concentration, float arcRadians)
    {
        if (!float.IsFinite(directionRadians) || !float.IsFinite(concentration) || !float.IsFinite(arcRadians) ||
            TerminatingOrDeleted(grid) || !HasComp<MapGridComponent>(grid))
            return false;
        var allocation = EnsureComp<WGShipShieldShuntComponent>(grid);
        concentration = Math.Clamp(concentration, 0f, 1f);
        arcRadians = Math.Clamp(arcRadians, WGShipShieldShuntMath.MinimumArc, WGShipShieldShuntMath.FullArc);
        LimitWolfgateShieldShunt(grid, ref concentration, ref arcRadians);
        allocation.TargetInitialized = true;
        allocation.TargetDirectionRadians = WGShipShieldShuntMath.NormalizeAngle(directionRadians);
        allocation.TargetConcentration = concentration;
        allocation.TargetArcRadians = arcRadians;
        _wgMovingShunts.Add(grid);
        return true;
    }

    /// <summary>Advances only moving allocations at a bounded ten updates per second.</summary>
    public void UpdateWolfgateShieldShunts(float frameTime)
    {
        _wgShuntAccumulator += frameTime;
        if (_wgShuntAccumulator < 0.1f)
            return;
        var dt = Math.Min(_wgShuntAccumulator, 0.2f);
        _wgShuntAccumulator = 0f;
        if (_wgMovingShunts.Count == 0)
            return;
        var finished = new List<EntityUid>();
        foreach (var grid in _wgMovingShunts)
        {
            if (TerminatingOrDeleted(grid) || !TryComp<WGShipShieldShuntComponent>(grid, out var allocation) ||
                !TryComp<MapGridComponent>(grid, out var mapGrid))
            {
                finished.Add(grid);
                continue;
            }
            ApplyWolfgateShieldShunt(grid, mapGrid, allocation,
                WGShipShieldShuntMath.StepAngle(allocation.DirectionRadians, allocation.TargetDirectionRadians, dt),
                WGShipShieldShuntMath.Step(allocation.Concentration, allocation.TargetConcentration, dt, WGShipShieldShuntMath.TransferSpeed),
                WGShipShieldShuntMath.Step(allocation.ArcRadians, allocation.TargetArcRadians, dt, WGShipShieldShuntMath.TurnSpeed));
            if (MathF.Abs(WGShipShieldShuntMath.NormalizeAngle(allocation.DirectionRadians - allocation.TargetDirectionRadians)) < 0.00001f &&
                allocation.Concentration == allocation.TargetConcentration && allocation.ArcRadians == allocation.TargetArcRadians)
                finished.Add(grid);
        }
        foreach (var grid in finished)
            _wgMovingShunts.Remove(grid);
    }

    /// <summary>Applies a ship-wide manual operating state without resetting recharge or damage.</summary>
    public bool SetWolfgateShieldEnabled(EntityUid grid, bool enabled)
    {
        if (TerminatingOrDeleted(grid) || !HasComp<MapGridComponent>(grid))
            return false;
        var allocation = EnsureComp<WGShipShieldShuntComponent>(grid);
        allocation.Enabled = enabled;
        DirtyField(grid, allocation, nameof(WGShipShieldShuntComponent.Enabled));
        if (!enabled && TryComp<ShipShieldedComponent>(grid, out var shielded))
        {
            if (shielded.Source is { } source && TryComp<ShipShieldEmitterComponent>(source, out var emitter))
            {
                if (RemoveWolfgateEmitterShield(source, emitter, "manual disable"))
                    PlayWolfgateShieldPowerSound(source, grid, false);
                emitter.Shield = null;
                emitter.Shielded = null;
            }
            else
                UnshieldEntity(grid, shielded);
        }
        return true;
    }

    /// <summary>Checks the persistent manual operating state for installed emitters.</summary>
    private bool IsWolfgateShieldEnabled(EntityUid grid)
    {
        return !TryComp<WGShipShieldShuntComponent>(grid, out var allocation) || allocation.Enabled;
    }

    /// <summary>Validates and applies one ship's shield power allocation.</summary>
    public bool SetWolfgateShieldShunt(EntityUid grid, float directionRadians, float concentration, float arcRadians)
    {
        if (!float.IsFinite(directionRadians) || !float.IsFinite(concentration) || !float.IsFinite(arcRadians) ||
            TerminatingOrDeleted(grid) || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return false;
        directionRadians = WGShipShieldShuntMath.NormalizeAngle(directionRadians);
        concentration = Math.Clamp(concentration, 0f, 1f);
        arcRadians = Math.Clamp(arcRadians, WGShipShieldShuntMath.MinimumArc, WGShipShieldShuntMath.FullArc);
        LimitWolfgateShieldShunt(grid, ref concentration, ref arcRadians);
        var allocation = EnsureComp<WGShipShieldShuntComponent>(grid);
        allocation.TargetInitialized = true;
        allocation.TargetDirectionRadians = directionRadians;
        allocation.TargetConcentration = concentration;
        allocation.TargetArcRadians = arcRadians;
        _wgMovingShunts.Remove(grid);
        ApplyWolfgateShieldShunt(grid, mapGrid, allocation, directionRadians, concentration, arcRadians);
        return true;
    }

    /// <summary>Updates active protection, rebuilding fixtures only when angular coverage is clipped.</summary>
    private void ApplyWolfgateShieldShunt(EntityUid grid, MapGridComponent mapGrid, WGShipShieldShuntComponent allocation,
        float directionRadians, float concentration, float arcRadians)
    {
        var wasClipped = allocation.Concentration >= 1f && allocation.ArcRadians < WGShipShieldShuntMath.FullArc - 0.00001f;
        var isClipped = concentration >= 1f && arcRadians < WGShipShieldShuntMath.FullArc - 0.00001f;
        if (allocation.DirectionRadians == directionRadians && allocation.Concentration == concentration &&
            allocation.ArcRadians == arcRadians && allocation.Center == mapGrid.LocalAABB.Center)
            return;
        allocation.DirectionRadians = directionRadians;
        allocation.Concentration = concentration;
        allocation.ArcRadians = arcRadians;
        allocation.Center = mapGrid.LocalAABB.Center;
        DirtyFields(grid, allocation, null, nameof(WGShipShieldShuntComponent.DirectionRadians),
            nameof(WGShipShieldShuntComponent.Concentration), nameof(WGShipShieldShuntComponent.ArcRadians),
            nameof(WGShipShieldShuntComponent.Center));
        if (TryComp<ShipShieldedComponent>(grid, out var shielded) &&
            TryComp<PhysicsComponent>(shielded.Shield, out var physics))
        {
            if (wasClipped || isClipped)
                CreateWolfgateShieldHull(shielded.Shield, grid, mapGrid, physics,
                    Comp<WGShipShieldVisualsComponent>(shielded.Shield).Contours);
            else
                SyncWolfgateShieldShunt(shielded.Shield, grid, mapGrid);
        }
    }

    /// <summary>Copies persistent ship allocation to the active shield.</summary>
    private WGShipShieldShuntComponent SyncWolfgateShieldShunt(EntityUid shield, EntityUid grid, MapGridComponent mapGrid)
    {
        var source = EnsureComp<WGShipShieldShuntComponent>(grid);
        EnforceWolfgateShieldArcLimit(shield, grid, source);
        source.Center = mapGrid.LocalAABB.Center;
        DirtyField(grid, source, nameof(WGShipShieldShuntComponent.Center));
        var target = EnsureComp<WGShipShieldShuntComponent>(shield);
        target.Center = source.Center;
        target.Enabled = source.Enabled;
        target.DirectionRadians = source.DirectionRadians;
        target.Concentration = source.Concentration;
        target.ArcRadians = source.ArcRadians;
        DirtyFields(shield, target, null, nameof(WGShipShieldShuntComponent.Center), nameof(WGShipShieldShuntComponent.Enabled),
            nameof(WGShipShieldShuntComponent.DirectionRadians), nameof(WGShipShieldShuntComponent.Concentration),
            nameof(WGShipShieldShuntComponent.ArcRadians));
        return target;
    }

    /// <summary>Only fully diverted shields need angular filtering during broadphase checks.</summary>
    private bool IsWolfgateShieldUnprotectedProjectile(EntityUid shield, EntityUid projectile)
    {
        if (!TryComp<WGShipShieldShuntComponent>(shield, out var allocation) || allocation.Concentration < 1f ||
            allocation.ArcRadians >= WGShipShieldShuntMath.FullArc - 0.00001f)
            return false;
        return WolfgateShieldStrength(shield, projectile) <= 0f;
    }
    /// <summary>Returns the allocation at the actual hull boundary nearest a projectile.</summary>
    private float WolfgateShieldStrength(EntityUid shield, EntityUid projectile)
    {
        if (!TryComp<WGShipShieldShuntComponent>(shield, out var allocation) || allocation.Concentration <= 0f ||
            allocation.ArcRadians >= WGShipShieldShuntMath.FullArc - 0.00001f ||
            !TryComp<WGShipShieldVisualsComponent>(shield, out var visuals))
            return 1f;
        var local = Vector2.Transform(_transformSystem.GetWorldPosition(projectile), _transformSystem.GetInvWorldMatrix(shield));
        var position = WGShipShieldGeometry.ClosestPoint(visuals.Contours, local);
        return WGShipShieldShuntMath.StrengthMultiplier(position, allocation.Center, allocation.DirectionRadians,
            allocation.Concentration, allocation.ArcRadians);
    }
}
