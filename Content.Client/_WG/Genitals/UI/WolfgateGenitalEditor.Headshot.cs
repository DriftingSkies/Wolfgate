using Content.Client._WG.Headshot;
using Content.Shared._WG.Headshot;

namespace Content.Client._WG.Genitals.UI;

/// <summary>The adult headshot button under the adult description, which opens the headshot URL editor.</summary>
public sealed partial class WolfgateGenitalEditor
{
    private HeadshotWindow? _nsfwHeadshotWindow;

    private void InitializeNsfwHeadshot()
    {
        NsfwHeadshotButton.OnPressed += _ => OpenNsfwHeadshotWindow();
    }

    /// <summary>Syncs the button with the anatomy profile.</summary>
    private void UpdateNsfwHeadshot()
    {
        NsfwHeadshotButton.Visible = _cfg.GetCVar(HeadshotCVars.Enabled);
        NsfwHeadshotButton.Text = Loc.GetString(_profile.NsfwHeadshotUrl.Length == 0
            ? "wg-genitals-nsfw-headshot-add"
            : "wg-genitals-nsfw-headshot-edit");
    }

    private void OpenNsfwHeadshotWindow()
    {
        CloseNsfwHeadshotWindow();

        _nsfwHeadshotWindow = new HeadshotWindow(_profile.NsfwHeadshotUrl,
            "wg-headshot-nsfw-window-title", "wg-headshot-nsfw-window-hint");
        _nsfwHeadshotWindow.OnUrlChanged += SetNsfwHeadshotUrl;
        _nsfwHeadshotWindow.OnClose += () => _nsfwHeadshotWindow = null;
        _nsfwHeadshotWindow.OpenCentered();
    }

    /// <summary>Hands the URL straight to the profile editor; like the text, it carries no organ rules.</summary>
    private void SetNsfwHeadshotUrl(string url)
    {
        if (url == _profile.NsfwHeadshotUrl)
            return;

        _profile = _profile.WithNsfwHeadshotUrl(url);
        UpdateNsfwHeadshot();
        OnProfileChanged?.Invoke(_profile);
    }

    private void CloseNsfwHeadshotWindow()
    {
        _nsfwHeadshotWindow?.Close();
        _nsfwHeadshotWindow = null;
    }
}
