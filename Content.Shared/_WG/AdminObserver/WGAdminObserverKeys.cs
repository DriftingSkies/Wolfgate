using Robust.Shared.Input;

namespace Content.Shared._WG.AdminObserver;

/// <summary>The admin observer's keys, listed under Options > Controls.</summary>
[KeyFunctions]
public static class WGAdminObserverKeys
{
    /// <summary>Runs <c>aghost</c>: into the admin observer, or back to the body it left.</summary>
    public static readonly BoundKeyFunction WGToggleAghost = "WGToggleAghost";

    /// <summary>Shows the admin observer to players, or hides it again.</summary>
    public static readonly BoundKeyFunction WGToggleManifest = "WGToggleManifest";

    /// <summary>Opens the observer panel.</summary>
    public static readonly BoundKeyFunction WGOpenObserverPanel = "WGOpenObserverPanel";
}
