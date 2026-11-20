using Robust.Shared.Input;

namespace Content.Shared._WG.CancelInteractions;

/// <summary>The key that cancels the player's own interactions in progress.</summary>
[KeyFunctions]
public static class WGCancelInteractionKeys
{
    /// <summary>Cancels every do-after the player is performing.</summary>
    public static readonly BoundKeyFunction WGCancelInteractions = "WGCancelInteractions";
}
