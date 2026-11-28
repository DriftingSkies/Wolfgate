using Content.Server._WG.BoardingAction.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.TractorBeam;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipModules;

public sealed partial class WGShipModuleSystem
{
    [Dependency] private ShuttleSystem _shuttle = default!;

    private readonly HashSet<EntityUid> _docked = new();

    /// <summary>
    /// Why a module that moves the hull can't: it is static, docked to anything, over a planet, or (with
    /// <paramref name="held"/>) held by a harpoon, hack or tractor beam.
    /// </summary>
    public WGShipModuleLock MotionLock(EntityUid grid, bool held)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body) || body.BodyType == BodyType.Static)
            return WGShipModuleLock.Anchored;

        if (HasComp<MapGridComponent>(Transform(grid).MapUid))
            return WGShipModuleLock.Planet;

        _docked.Clear();
        _shuttle.GetAllDockedShuttlesIgnoringFTLLock(grid, _docked);
        if (_docked.Count > 1)
            return WGShipModuleLock.Docked;

        if (held && IsHeld(grid))
            return WGShipModuleLock.Held;

        return WGShipModuleLock.None;
    }

    /// <summary>A kit for a module carried anywhere aboard a ship, packed in a crate or not.</summary>
    public EntityUid? FindKit(EntityUid grid, ProtoId<WGShipModulePrototype> module)
    {
        var kits = EntityQueryEnumerator<WGShipModuleKitComponent, TransformComponent>();
        while (kits.MoveNext(out var uid, out var kit, out var xform))
        {
            if (kit.Module == module && xform.GridUid == grid && !TerminatingOrDeleted(uid))
                return uid;
        }

        return null;
    }

    /// <summary>Whether a ship can have a module fitted: it is free, or a kit for it is aboard.</summary>
    public bool HasKitFor(EntityUid grid, WGShipModulePrototype module)
    {
        return module.Kit == null || FindKit(grid, module.ID) != null;
    }

    /// <summary>Whether a harpoon, a hack or a tractor beam holds the ship.</summary>
    private bool IsHeld(EntityUid grid)
    {
        if (TryComp<WGFtlDisruptedComponent>(grid, out var disrupted) && disrupted.Sources.Count > 0)
            return true;

        var beams = EntityQueryEnumerator<TractorBeamEmitterComponent>();
        while (beams.MoveNext(out _, out var beam))
        {
            if (beam.Target is { } target && (target == grid || Transform(target).GridUid == grid))
                return true;
        }

        return false;
    }
}
