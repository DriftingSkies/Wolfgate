using Content.Server._WG.Remade.Conversion;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>The helpers abilities use to turn a body into a Remade.</summary>
public sealed partial class WGAbilitySystem
{
    [Dependency] private WGRemadeConversionSystem _conversion = default!;

    /// <summary>Sets a Remade to turn a body, staying with it until it bursts. Returns whether the throes began.</summary>
    public bool Turn(EntityUid corpse, EntityUid reanimator)
    {
        var source = CompOrNull<WGRemadeComponent>(reanimator)?.Source;
        return _conversion.TryStartConversion(corpse, source, reanimator);
    }

    /// <summary>Whether the body is in the throes of turning.</summary>
    public bool IsTurning(EntityUid corpse)
    {
        return !TerminatingOrDeleted(corpse) && HasComp<WGRemadeConvertingComponent>(corpse);
    }
}
