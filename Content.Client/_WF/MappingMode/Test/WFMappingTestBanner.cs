using System.Numerics;
using Content.Client.Stylesheets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.MappingMode.Test;

/// <summary>
/// The strip over the gameplay screen while the player tests their map: what they are testing, Back to mapping and
/// the time left. The client system mounts it on the active screen and feeds it every frame.
/// </summary>
public sealed class WFMappingTestBanner : PanelContainer
{
    private readonly Label _title;
    private readonly Label _time;

    /// <summary>The Back to mapping button.</summary>
    public readonly Button BackButton;

    public WFMappingTestBanner()
    {
        Name = "WFMappingTestBanner";
        StyleClasses.Add(StyleBase.ClassAngleRect);
        HorizontalAlignment = HAlignment.Center;
        VerticalAlignment = VAlignment.Top;
        Margin = new Thickness(0, 4, 0, 0);
        MouseFilter = MouseFilterMode.Stop;

        _title = new Label { VerticalAlignment = VAlignment.Center };
        _time = new Label { VerticalAlignment = VAlignment.Center, MinWidth = 110 };
        BackButton = new Button
        {
            Text = Loc.GetString("wf-mapping-test-banner-back"),
            StyleClasses = { StyleBase.ButtonSquare },
        };

        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(8, 2),
            SeparationOverride = 12,
            Children = { _title, BackButton, _time },
        });
    }

    /// <summary>Shows the map's name, the time left and the key that goes back, if one is bound.</summary>
    public void Update(string mapName, TimeSpan remaining, string? key)
    {
        _title.Text = Loc.GetString("wf-mapping-test-banner-title", ("name", mapName));
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        _time.Text = Loc.GetString("wf-mapping-test-banner-time",
            ("time", $"{(int) remaining.TotalMinutes:00}:{remaining.Seconds:00}"));
        BackButton.Text = key == null
            ? Loc.GetString("wf-mapping-test-banner-back")
            : Loc.GetString("wf-mapping-test-banner-back-key", ("key", key));
    }
}
