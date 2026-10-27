using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Organ;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;

namespace Content.Server._WF.Wolfmed.Body;

/// <summary>Marks an organ that failed at 0 health and was left in the body rather than torn out.</summary>
[RegisterComponent]
public sealed partial class WolfmedOrganFailedComponent : Component;

/// <summary>
/// Keeps a failed organ in its slot at 0 health, so a medic mends it with the organ heal surgery instead of
/// needing a spare. Its destruction wound still lands once. A chassis's parts are swapped, not mended, so a
/// mechanical body keeps Onyx's removal, as does every body with wolfmed.organ_destruction_removes on.
/// </summary>
public sealed class WolfmedOrganFailureSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;

    /// <summary>
    /// Called by OrganHealthSystem for every non-brain organ at 0 health. True when the organ stays where it is,
    /// false when it should be destroyed as Onyx does.
    /// </summary>
    public bool Keep(Entity<WolfmedOrganComponent, OrganComponent> organ)
    {
        if (_config.GetCVar(WolfmedCVars.OrganDestructionRemoves) ||
            organ.Comp2.Body is { } body && _shutdown.IsMechanical(body))
            return false;

        if (HasComp<WolfmedOrganFailedComponent>(organ))
            return true;

        AddComp<WolfmedOrganFailedComponent>(organ);
        if (organ.Comp1.DestructionWound is { } wound &&
            organ.Comp1.DestructionWoundSeverity > FixedPoint2.Zero &&
            Transform(organ).ParentUid is { Valid: true } part &&
            !TerminatingOrDeleted(part) &&
            HasComp<WoundableComponent>(part))
            _wounds.CreateOrMergeWound(part, wound, organ.Comp1.DestructionWoundSeverity);

        return true;
    }

    /// <summary>A mended organ can fail again, and wound its part again when it does.</summary>
    public void Restored(EntityUid organ) => RemComp<WolfmedOrganFailedComponent>(organ);
}
