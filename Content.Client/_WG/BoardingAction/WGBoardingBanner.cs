using Content.Shared._WG.BoardingAction;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.BoardingAction;

/// <summary>What the helm shows about hacks and disruption: threats to this ship, then what this ship is doing itself.</summary>
public sealed class WGBoardingBanner : BoxContainer
{
    private static readonly Color WarningText = Color.FromHex("#ff9a8a");
    private static readonly Color NoticeText = Color.FromHex("#9ff2ff");

    /// <summary>Room for the release button's label under any console skin.</summary>
    public const float ReleaseWidth = 140f;

    private readonly PanelContainer _warning;
    private readonly RichTextLabel _warningLabel;
    private readonly PanelContainer _notice;
    private readonly RichTextLabel _noticeLabel;
    private readonly Button _release;

    public event Action? ReleaseRequested;

    public WGBoardingBanner()
    {
        Visible = false;
        HorizontalExpand = true;
        Orientation = LayoutOrientation.Vertical;

        AddChild(_warning = Panel("#40120d", "#d94a38"));
        _warning.AddChild(_warningLabel = new RichTextLabel { Margin = new Thickness(10, 7), HorizontalExpand = true });

        AddChild(_notice = Panel("#0d3036", "#3fa7b5"));
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, HorizontalExpand = true };
        _notice.AddChild(row);
        row.AddChild(_noticeLabel = new RichTextLabel { Margin = new Thickness(10, 7), HorizontalExpand = true });
        row.AddChild(_release = new Button
        {
            Text = Loc.GetString("wg-boarding-helm-release"),
            // The console theme clips button text, which stops the label holding the button open.
            MinWidth = ReleaseWidth,
            Margin = new Thickness(0, 4, 6, 4),
            VerticalAlignment = VAlignment.Center,
        });
        _release.OnPressed += _ => ReleaseRequested?.Invoke();
    }

    private static PanelContainer Panel(string background, string border)
    {
        return new PanelContainer
        {
            Visible = false,
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex(background),
                BorderColor = Color.FromHex(border),
                BorderThickness = new Thickness(1),
            },
        };
    }

    public void SetState(WGBoardingHelmState? state)
    {
        var warnings = new List<string>();
        var notices = new List<string>();
        if (state != null)
        {
            if (state.Hackers.Length > 0)
            {
                warnings.Add(Loc.GetString(state.Lockdown ? "wg-boarding-helm-lockdown" : "wg-boarding-helm-hack",
                    ("ships", string.Join(", ", state.Hackers))));
            }

            if (state.CableTarget is { } cable)
                notices.Add(Loc.GetString("wg-boarding-helm-cable", ("ship", cable)));
        }

        // SetMessage(string) treats ship names as literal text, including brackets or markup.
        _warning.Visible = warnings.Count > 0;
        _warningLabel.SetMessage(string.Join("\n", warnings), WarningText);
        _notice.Visible = notices.Count > 0;
        _noticeLabel.SetMessage(string.Join("\n", notices), NoticeText);
        _release.Visible = state?.CableTarget != null;
        Visible = _warning.Visible || _notice.Visible;
    }
}
