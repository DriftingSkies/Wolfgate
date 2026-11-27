using Content.Server._WG.Pyramid;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCUtilitySystem
{
    /// <summary>The Pyramid has no mob state, but is a live target until it starts to collapse.</summary>
    private bool WgIsLivePyramid(EntityUid target)
    {
        return TryComp<WGPyramidComponent>(target, out var pyramid) && pyramid.Collapsing == null;
    }
}
