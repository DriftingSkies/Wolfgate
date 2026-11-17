using System.Diagnostics.CodeAnalysis;
using Content.Shared.Shuttles.BUIStates;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>How long consoles share one walk of the docks. Anchoring changes raise nothing, so they wait this long.</summary>
    private static readonly TimeSpan WGDocksLifetime = TimeSpan.FromSeconds(0.5);

    private Dictionary<NetEntity, List<DockingPortState>>? _wgDocks;
    private TimeSpan _wgDocksUntil;

    private bool WGTryGetCachedDocks([NotNullWhen(true)] out Dictionary<NetEntity, List<DockingPortState>>? docks)
    {
        docks = _wgDocks;
        return docks != null && _wgCockpitTiming.CurTime < _wgDocksUntil;
    }

    private void WGCacheDocks(Dictionary<NetEntity, List<DockingPortState>> docks)
    {
        _wgDocks = docks;
        _wgDocksUntil = _wgCockpitTiming.CurTime + WGDocksLifetime;
    }

    /// <summary>Drops the shared walk when something consoles should show at once has changed.</summary>
    public void WGForgetDocks()
    {
        _wgDocks = null;
    }
}
