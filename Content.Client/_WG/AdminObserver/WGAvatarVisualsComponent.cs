using Content.Shared._WG.AdminObserver;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.AdminObserver;

/// <summary>What this client last drew a manifested observer as, so it redraws only when that changes.</summary>
[RegisterComponent]
public sealed partial class WGAvatarVisualsComponent : Component
{
    public WGAvatarLook? Look;

    /// <summary>Whether the observer's sprite is currently something other than its own.</summary>
    public bool Replaced;

    /// <summary>When a character avatar's sprite was last copied from its body.</summary>
    public TimeSpan LastCopy;
}

/// <summary>Everything that decides a manifested observer's sprite.</summary>
public readonly record struct WGAvatarLook(WGAvatarKind Kind, EntProtoId? Prototype, EntityUid? Body, bool Hologram);
