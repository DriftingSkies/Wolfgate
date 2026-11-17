using Content.Shared._WG.ShipCommand;

namespace Content.Shared.Shuttles.Systems;

/// <summary>FTL limits an unrestricted grid (<see cref="WGFtlUnrestrictedComponent"/>) skips.</summary>
public abstract partial class SharedShuttleSystem
{
    /// <summary>Whether the grid may jump with other ships' helms close by.</summary>
    public bool WgIgnoresFtlProximity(EntityUid grid)
    {
        return TryComp<WGFtlUnrestrictedComponent>(grid, out var comp) && comp.IgnoreProximity;
    }

    /// <summary>Whether the grid may jump past the FTL mass limit.</summary>
    public bool WgIgnoresFtlMassLimit(EntityUid grid)
    {
        return TryComp<WGFtlUnrestrictedComponent>(grid, out var comp) && comp.IgnoreMassLimit;
    }

    /// <summary>Whether the grid may jump into exclusion zones.</summary>
    public bool WgIgnoresFtlExclusionZones(EntityUid grid)
    {
        return TryComp<WGFtlUnrestrictedComponent>(grid, out var comp) && comp.IgnoreExclusionZones;
    }
}
