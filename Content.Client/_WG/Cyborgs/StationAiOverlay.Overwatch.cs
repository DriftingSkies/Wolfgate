using Content.Shared._Rat.Overwatch;
using Robust.Client.Graphics;

namespace Content.Client.Silicons.StationAi;

public sealed partial class StationAiOverlay
{
    /// <summary>
    /// Skips the camera static while the AI views an overwatch feed, which is usually off the core's grid.
    /// </summary>
    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return !_entManager.TryGetComponent(_player.LocalEntity, out RatOverwatchWatchingComponent? watching)
            || watching.Watching == null;
    }
}
