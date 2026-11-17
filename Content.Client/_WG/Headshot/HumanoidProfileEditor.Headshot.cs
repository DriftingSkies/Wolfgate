using Content.Client._WG.Headshot;
using Content.Shared._WG.Headshot;

namespace Content.Client.Lobby.UI;

/// <summary>The Headshot button under the description, which opens the headshot URL editor.</summary>
public sealed partial class HumanoidProfileEditor
{
    private HeadshotWindow? _headshotWindow;

    private void InitializeHeadshot()
    {
        HeadshotButton.OnPressed += _ => OpenHeadshotWindow();
        UpdateHeadshot();
    }

    /// <summary>Syncs the button with the loaded profile and closes an editor opened for the previous one.</summary>
    private void UpdateHeadshot()
    {
        CloseHeadshotWindow();
        HeadshotButton.Visible = _cfgManager.GetCVar(HeadshotCVars.Enabled);
        HeadshotButton.Text = Loc.GetString(string.IsNullOrEmpty(Profile?.HeadshotUrl)
            ? "wg-headshot-editor-button-add"
            : "wg-headshot-editor-button-edit");
    }

    private void OpenHeadshotWindow()
    {
        CloseHeadshotWindow();

        _headshotWindow = new HeadshotWindow(Profile?.HeadshotUrl ?? string.Empty);
        _headshotWindow.OnUrlChanged += SetHeadshotUrl;
        _headshotWindow.OnClose += () => _headshotWindow = null;
        _headshotWindow.OpenCentered();
    }

    private void SetHeadshotUrl(string url)
    {
        if (Profile == null || Profile.HeadshotUrl == url)
            return;

        Profile = Profile.WithHeadshotUrl(url);
        HeadshotButton.Text = Loc.GetString(url.Length == 0
            ? "wg-headshot-editor-button-add"
            : "wg-headshot-editor-button-edit");
        SetDirty();
    }

    private void CloseHeadshotWindow()
    {
        _headshotWindow?.Close();
        _headshotWindow = null;
    }
}
