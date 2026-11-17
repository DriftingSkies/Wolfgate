using Content.Client._WG.AsteroidMining;
using Content.Client._WG.Cockpit;
using Content.Shared._WG.AsteroidMining;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private WGCockpitMiningPage? _wgCockpitMining;

    /// <summary>The last mining scan sent through this helm, so a cockpit opened again starts from it.</summary>
    public WGMiningBeamGunneryState WgCockpitMiningState { get; private set; }

    /// <summary>Builds the cockpit's mining page, which sets the lasers through the helm's gunnery link.</summary>
    private Control WgCockpitMiningPage(WGCockpitLease lease)
    {
        var page = new WGCockpitMiningPage();
        page.PowerRequested += power => WgSendCockpitGunnery(new WGMiningBeamPowerMessage(power));
        page.UpdateState(WgCockpitMiningState);
        _wgCockpitMining = page;
        lease.Remember(() =>
        {
            if (_wgCockpitMining == page)
                _wgCockpitMining = null;
        });
        return page;
    }

    /// <summary>Applies a mining scan sent through the helm to the cockpit's mining page.</summary>
    public void WgReceiveCockpitMining(WGMiningBeamGunneryState state)
    {
        WgCockpitMiningState = state;
        _wgCockpitMining?.UpdateState(state);
    }
}
