using Content.Client._WG.Cockpit;
using Content.Shared.Shuttles.Systems;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    /// <summary>Reads the latest authoritative FTL snapshot, including the drive, for the permanent status bank.</summary>
    public WGCockpitStatusReading WgCockpitFtlStatus() =>
        WGCockpitStatusReading.Ftl(_shuttleEntity == null ? FTLState.Invalid : _state, _wgDrive);
}
