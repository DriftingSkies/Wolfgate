using Content.Client._WG.CombatConsole;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.RadarMenu;

/// <summary>
/// The radar's right-click menu: a console-styled card with a heading, a readout line and a column of keys. A key can
/// open a second page in place, such as the list of docks, with a key back.
/// </summary>
public sealed class WGRadarContextMenu : Popup
{
    private const float Width = 220f;

    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly BoxContainer _keys;
    private readonly List<Action> _closers = new();

    public WGRadarContextMenu(string title, string subtitle)
    {
        _title = new Label
        {
            Text = title,
            FontColorOverride = WGInstrumentTheme.Accent,
            ClipText = true,
        };
        _subtitle = new Label
        {
            Text = subtitle,
            FontColorOverride = WGInstrumentTheme.Muted,
            ClipText = true,
        };
        _keys = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            MinWidth = Width,
            MaxWidth = Width,
        };
        body.AddChild(_title);
        body.AddChild(_subtitle);
        body.AddChild(_keys);

        var panel = new WGInstrumentPanel(8);
        panel.AddChild(body);
        AddChild(panel);

        OnPopupHide += () =>
        {
            foreach (var closer in _closers)
                closer();
            _closers.Clear();
        };
    }

    /// <summary>Adds a key that runs an action and closes the menu, or one that is greyed out.</summary>
    public Button AddKey(string text, Action? action, bool closes = true)
    {
        var key = new Button { Text = text, HorizontalExpand = true, Disabled = action == null };
        key.AddStyleClass("WgCompact");
        key.AddStyleClass("WgWeapon");
        WGInstrumentTheme.Switch(key);
        if (action != null)
        {
            key.OnPressed += _ =>
            {
                if (closes)
                    Close();
                action();
            };
        }

        _keys.AddChild(key);
        return key;
    }

    /// <summary>A line of plain text among the keys, such as "Checking docks".</summary>
    public void AddNote(string text)
    {
        _keys.AddChild(new Label { Text = text, FontColorOverride = WGInstrumentTheme.Muted, ClipText = true });
    }

    /// <summary>Clears the keys for a new page, keeping the heading.</summary>
    public void NewPage(string subtitle)
    {
        _subtitle.Text = subtitle;
        _keys.RemoveAllChildren();
    }

    /// <summary>Runs when the menu closes, to drop listeners it no longer needs.</summary>
    public void OnClosed(Action closer) => _closers.Add(closer);
}
