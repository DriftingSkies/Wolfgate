namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private void WfSectorInitialize()
    {
        SectorModeButton.OnPressed += _ => SwitchMode(ShuttleConsoleMode.Sector);
        SectorModeButton.Group = NavModeButton.Group;
    }

    private void WfSetSectorMode(bool active)
    {
        SectorContainer.Visible = active;
    }

    /// <summary>Points the sector map at the console, so it shows the map the console is on.</summary>
    private void WfSectorUpdateState(EntityUid console)
    {
        SectorContainer.MapControl.Tracked = console;
    }
}
