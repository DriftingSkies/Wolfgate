using Robust.Shared.Map;

namespace Content.Server._WG.Remade.Director;

/// <summary>What the morgue system asks of the Director: an Infector to send, and a feed for its biomass.</summary>
public sealed partial class WGRemadeDirectorSystem
{
    /// <summary>Sends a free Infector of a Monolith's horde to a place. Returns it, or null if none is free on that grid.</summary>
    public EntityUid? SendInfector(EntityUid monolith, EntityCoordinates at)
    {
        if (!TryComp<WGRemadeDirectorComponent>(monolith, out var director) || FindBuilder(director, at) is not { } infector)
            return null;

        SetKnown(infector, Comp<WGRemadeBehaviourComponent>(infector), at, _timing.CurTime);
        return infector;
    }

    /// <summary>Biomass for a body the Monolith turned out of a morgue.</summary>
    public void Feed(EntityUid monolith, float biomass)
    {
        if (TryComp<WGRemadeDirectorComponent>(monolith, out var director))
            director.Biomass += biomass;
    }
}
