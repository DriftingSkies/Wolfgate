namespace Content.Client._NF.LateJoin.Windows;

public sealed partial class PickerWindow
{
    /// <summary>Opens the ordinary job list directly from Round Setup's normal-join choice.</summary>
    public void OpenNormalJobs()
    {
        OpenCentered();
        SetCurrentTab(PickerType.Station);
    }
}
