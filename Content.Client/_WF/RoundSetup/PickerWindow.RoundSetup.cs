namespace Content.Client._NF.LateJoin.Windows;

public sealed partial class PickerWindow
{
    private void InitializeStationOnly()
    {
        CrewTabButton.Visible = false;
        StationTabButton.ToolTip = Loc.GetString("wf-round-setup-standard-stations-help");
    }

    /// <summary>Opens the existing join picker directly to standard station jobs.</summary>
    public void OpenNormalJobs()
    {
        OpenCentered();
    }
}
