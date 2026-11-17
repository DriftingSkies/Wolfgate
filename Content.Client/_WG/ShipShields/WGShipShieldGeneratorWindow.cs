using System.Globalization;
using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WG.ShipShields;

/// <summary>Hosts the shared ship-local shield controls outside the helm.</summary>
public sealed class WGShipShieldGeneratorWindow : DefaultWindow
{
    /// <summary>The shared shield controls shown in this panel.</summary>
    public WGShipShieldShuntScreen ShieldPanel { get; } = new();

    /// <summary>Someone asked for a new field strength.</summary>
    public event Action<float>? StrengthRequested;

    private readonly BoxContainer _strengthRow;
    private readonly Label _strengthLabel;
    private readonly LineEdit _strengthEntry;

    /// <summary>Creates a generator panel with independent preview and settings scrolling.</summary>
    public WGShipShieldGeneratorWindow()
    {
        Title = Loc.GetString("wg-shield-generator-title");
        MinSize = new Vector2(760f, 540f);
        SetSize = new Vector2(900f, 700f);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        ShieldPanel.VerticalExpand = true;
        root.AddChild(ShieldPanel);

        _strengthLabel = new Label { HorizontalExpand = true };
        _strengthEntry = new LineEdit
        {
            MinWidth = 160f,
            IsValid = text => text.Length <= 16 && text.All(char.IsAsciiDigit),
            PlaceHolder = Loc.GetString("wg-shield-generator-strength-placeholder"),
        };
        var apply = new Button { Text = Loc.GetString("wg-shield-generator-strength-apply") };
        apply.OnPressed += _ => Apply();
        _strengthEntry.OnTextEntered += _ => Apply();

        _strengthRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Margin = new Thickness(4f, 0f, 4f, 4f),
            Visible = false,
        };
        _strengthRow.AddChild(_strengthLabel);
        _strengthRow.AddChild(_strengthEntry);
        _strengthRow.AddChild(apply);
        root.AddChild(_strengthRow);
        Contents.AddChild(root);
    }

    /// <summary>Shows the strength control, for a generator that has one.</summary>
    public void ShowStrength(float strength)
    {
        // The window grows by the row once, so the panels above keep their height; a later state leaves a size the
        // user chose alone.
        if (!_strengthRow.Visible)
            SetSize = new Vector2(900f, 744f);
        _strengthRow.Visible = true;
        _strengthLabel.Text = Loc.GetString("wg-shield-generator-strength", ("strength", strength.ToString("N0", CultureInfo.InvariantCulture)));
    }

    private void Apply()
    {
        if (!float.TryParse(_strengthEntry.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var strength) || strength <= 0f)
            return;

        StrengthRequested?.Invoke(strength);
        _strengthEntry.Clear();
    }
}
