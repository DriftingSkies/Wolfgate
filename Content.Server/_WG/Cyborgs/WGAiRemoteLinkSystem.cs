using Content.Shared._CorvaxNext.Silicons.Borgs.Components;

namespace Content.Server._WG.Cyborgs;

/// <summary>
/// Decides which networked borgs an AI can reach: any on its core's grid, plus the ones it has already controlled
/// anywhere on the same map. Pulling the borg's network device breaks the link.
/// </summary>
public sealed class WGAiRemoteLinkSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AiRemoteControllerComponent, ComponentRemove>(OnControllerRemoved);
    }

    private void OnControllerRemoved(EntityUid uid, AiRemoteControllerComponent component, ComponentRemove args)
    {
        RemCompDeferred<WGAiRemoteLinkComponent>(uid);
    }

    /// <summary>Whether the AI with this brain can list and take control of the device.</summary>
    public bool CanReach(EntityUid ai, EntityUid device)
    {
        var aiXform = Transform(ai);
        var deviceXform = Transform(device);

        if (aiXform.GridUid != null && aiXform.GridUid == deviceXform.GridUid)
            return true;

        return aiXform.MapID == deviceXform.MapID
            && TryComp<WGAiRemoteLinkComponent>(device, out var link)
            && link.Ai == ai;
    }

    /// <summary>Links the device to the AI that just took control of it, replacing any earlier link.</summary>
    public void Link(EntityUid ai, EntityUid device)
    {
        EnsureComp<WGAiRemoteLinkComponent>(device).Ai = ai;
    }
}
