using Content.Client._WG.Shuttles.UI;
using Content.Shared._WG.Shuttles;
using Content.Shared.Shuttles.Systems;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    private readonly WGFtlDriveNotice _wgDriveNotice = new();
    private WGFtlDriveStatus _wgDrive = WGFtlDriveStatus.Ready;

    /// <summary>The server's last report of the ship's FTL drive; ready until it reports.</summary>
    public WGFtlDriveStatus WgFtlDrive => _wgDrive;

    /// <summary>The notice that shows on the page while the ship cannot jump.</summary>
    public WGFtlDriveNotice WgDriveNotice => _wgDriveNotice;

    /// <summary>True while the ship has no powered FTL drive.</summary>
    private bool WgFtlDriveBlocked => _wgDrive != WGFtlDriveStatus.Ready;

    /// <summary>Applies the server's drive status to the notice, the FTL button's tooltip and the state readout.</summary>
    private void WgUpdateFtlDrive(WGFtlDriveStatus status)
    {
        _wgDrive = status;
        _wgDriveNotice.SetStatus(status);
        MapFTLButton.ToolTip = WGFtlDriveNotice.Reason(status);
        if (WgFtlDriveBlocked && _state == FTLState.Available)
            MapFTLState.Text = Loc.GetString("wg-ftl-state-offline");
    }

    /// <summary>Disables the FTL button and ends an FTL preview, leaving autopilot targeting alone.</summary>
    private void WgBlockFtl()
    {
        if (MapFTLButton.Pressed)
        {
            MapFTLButton.Pressed = false;
            MapRadar.FtlMode = false;
        }
        MapRadar.ShowFTLRangeOnly = false;
        MapFTLButton.Disabled = true;
    }
}
