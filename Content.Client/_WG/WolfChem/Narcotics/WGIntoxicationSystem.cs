using Content.Shared._WG.WolfChem.Narcotics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Timing;

namespace Content.Client._WG.WolfChem.Narcotics;

/// <summary>
/// Client half of intoxication: keeps predicted movement speed in step with the networked levels and draws the
/// screen overlay.
/// </summary>
public sealed partial class WGIntoxicationSystem : SharedWGIntoxicationSystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Seconds a nod takes to shut the eyes.</summary>
    private const float NodCloseSeconds = 0.6f;

    private WGIntoxicationOverlay _overlay = default!;
    private WGNodOverlay _nodOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new WGIntoxicationOverlay(this);
        _overlays.AddOverlay(_overlay);
        _nodOverlay = new WGNodOverlay(this);
        _overlays.AddOverlay(_nodOverlay);
        SubscribeLocalEvent<WGIntoxicationComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WGIntoxicationComponent, ComponentRemove>(OnRemove);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
        _overlays.RemoveOverlay(_nodOverlay);
    }

    /// <summary>How far a nod has shut the local player's eyes: 0 awake, 1 shut.</summary>
    public float NodClosure()
    {
        if (_player.LocalEntity is not { } local
            || !TryComp<WGIntoxicationComponent>(local, out var comp)
            || !IsNodding(comp))
            return 0f;

        return Math.Clamp((float) (_timing.CurTime - comp.NodStart).TotalSeconds / NodCloseSeconds, 0f, 1f);
    }

    /// <summary>How far past its start the overdose tier ramps to full strength, as a share of its start level.</summary>
    private const float OverdoseRamp = 0.5f;

    /// <summary>
    /// The high that sets the look: the furthest phase, then the strongest within it. Null when sober or when nothing
    /// active has a look.
    /// </summary>
    public WGDominantHigh? DominantHigh(WGIntoxicationComponent comp)
    {
        WGDominantHigh? best = null;
        foreach (var (profile, level) in comp.Levels)
        {
            if (!Proto.TryIndex(profile, out var proto))
                continue;

            var tier = proto.TierAt(level);
            var phase = proto.PhaseOf(tier);
            if (phase == WGDrugPhase.None || proto.Tiers[tier].Overlay <= 0f)
                continue;

            var strength = Strength(proto, tier, level);
            if (best is { } current && (phase < current.Phase || phase == current.Phase && strength <= current.Strength))
                continue;

            best = new WGDominantHigh(proto.Family, phase, strength, tier);
        }

        return best;
    }

    /// <summary>The look's strength at a level: from this tier's overlay towards the next tier's as the level climbs.</summary>
    private static float Strength(WGDrugProfilePrototype proto, int tier, float level)
    {
        var current = proto.Tiers[tier];
        float next, span;
        if (tier + 1 < proto.Tiers.Count)
        {
            next = proto.Tiers[tier + 1].Overlay;
            span = proto.Tiers[tier + 1].Min - current.Min;
        }
        else
        {
            next = 1f;
            span = current.Min * OverdoseRamp;
        }

        var progress = span > 0f ? Math.Clamp((level - current.Min) / span, 0f, 1f) : 1f;
        return Math.Clamp(current.Overlay + (Math.Max(next, current.Overlay) - current.Overlay) * progress, 0f, 1f);
    }

    private void OnState(Entity<WGIntoxicationComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnRemove(Entity<WGIntoxicationComponent> ent, ref ComponentRemove args)
    {
        ent.Comp.Levels.Clear();
        ent.Comp.Crashes.Clear();
        if (!TerminatingOrDeleted(ent))
            Movement.RefreshMovementSpeedModifiers(ent);
    }
}

/// <summary>The high that sets how the world looks: its family, phase, look strength (0-1) and tier.</summary>
public readonly record struct WGDominantHigh(WGDrugFamily Family, WGDrugPhase Phase, float Strength, int Tier);
