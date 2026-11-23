using System.Numerics;

namespace Content.Server._WG.Cyborgs;

/// <summary>
/// On a watcher whose zoom limit was lowered for an overwatch camera feed; holds the limit to restore afterwards.
/// </summary>
[RegisterComponent]
public sealed partial class WGOverwatchZoomLimitComponent : Component
{
    /// <summary>
    /// The watcher's own maximum zoom before the feed.
    /// </summary>
    [ViewVariables]
    public Vector2 MaxZoom;
}
