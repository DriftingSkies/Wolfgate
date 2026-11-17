using Content.Server._WG.Cockpit;
using Content.Shared._WG.Shuttles;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.Shuttles.Systems;

public sealed partial class ShuttleCameraSystem
{
    private bool WgCockpitCameraActive(EntityUid actor, EntityUid helm) =>
        EntityManager.System<WGCockpitGunnerySystem>().HasCameraSession(actor, helm);

    /// <summary>Returns a cockpit's camera only to the same connected pilot still operating its helm.</summary>
    public void WgRestoreCockpitCamera(EntityUid actor, EntityUid helm, ShuttleCameraView view, float zoom, bool lowLight)
    {
        if (TerminatingOrDeleted(actor) || !TryComp<PilotComponent>(actor, out var pilot) || pilot.Console != helm ||
            !TryComp<ActorComponent>(actor, out var player) || player.PlayerSession.AttachedEntity != actor)
            return;
        Apply(actor, helm, view, zoom, lowLight);
    }
}
