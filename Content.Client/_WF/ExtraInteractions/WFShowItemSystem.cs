using Content.Shared._WF.ExtraInteractions;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.ExtraInteractions;

/// <summary>
/// Keeps the item bubbles the server announced until they expire; <see cref="WFShowItemOverlay"/> draws them.
/// </summary>
public sealed class WFShowItemSystem : SharedWFShowItemSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;

    /// <summary>Bubbles up, by the mob showing the item.</summary>
    public readonly Dictionary<EntityUid, WFShowItemBubble> Bubbles = new();

    private readonly List<EntityUid> _expired = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFShowItemEvent>(OnShow);
        _overlays.AddOverlay(new WFShowItemOverlay(EntityManager));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFShowItemOverlay>();
    }

    private void OnShow(WFShowItemEvent ev)
    {
        if (!TryGetEntity(ev.User, out var user))
            return;

        TryGetEntity(ev.Item, out var item);
        Bubbles[user.Value] = new WFShowItemBubble(item, ev.Prototype, _timing.CurTime + BubbleDuration);
    }

    public override void FrameUpdate(float frameTime)
    {
        foreach (var (user, bubble) in Bubbles)
        {
            if (_timing.CurTime > bubble.End || TerminatingOrDeleted(user))
                _expired.Add(user);
        }

        foreach (var user in _expired)
        {
            Bubbles.Remove(user);
        }

        _expired.Clear();
    }
}

/// <summary>An item shown over a mob until <see cref="End"/>.</summary>
public readonly record struct WFShowItemBubble(EntityUid? Item, EntProtoId? Prototype, TimeSpan End);
