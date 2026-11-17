using Content.Shared._WG.PlanetNatives;
using Content.Shared.Mind.Components;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    /// <summary>Whether both are mindless planet natives, which leave each other alone whatever their factions.</summary>
    private bool WgNativeTruce(EntityUid ent, EntityUid target)
    {
        return HasComp<WGPlanetNativeComponent>(ent)
               && HasComp<WGPlanetNativeComponent>(target)
               && !(TryComp<MindContainerComponent>(ent, out var own) && own.HasMind)
               && !(TryComp<MindContainerComponent>(target, out var other) && other.HasMind);
    }
}
