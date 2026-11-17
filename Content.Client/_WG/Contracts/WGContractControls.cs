using System.Linq;
using System.Numerics;
using Content.Shared._WG.Contracts;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WG.Contracts;

/// <summary>Colours and wording shared by the board and the PDA app.</summary>
public static class WGContractStyle
{
    public static readonly Color Panel = Color.FromHex("#14191f");
    public static readonly Color Card = Color.FromHex("#1b222a");
    public static readonly Color CardSelected = Color.FromHex("#23344a");
    public static readonly Color Accent = Color.FromHex("#5fc3e4");
    public static readonly Color Bright = Color.FromHex("#e8ecef");
    public static readonly Color Dim = Color.FromHex("#8b98a5");
    public static readonly Color Good = Color.FromHex("#7fd18b");
    public static readonly Color Bad = Color.FromHex("#e07070");
    public static readonly Color Warn = Color.FromHex("#e0b050");

    /// <summary>Tier colours, 1 to 5, from safe to deadly.</summary>
    private static readonly Color[] TierColors =
    [
        Color.FromHex("#7fd18b"), Color.FromHex("#b5d97a"), Color.FromHex("#e0c060"), Color.FromHex("#e09050"),
        Color.FromHex("#e06060"),
    ];

    public static Color TierColor(int tier) => TierColors[Math.Clamp(tier - 1, 0, TierColors.Length - 1)];

    public static string Category(WGContractCategory category) =>
        Loc.GetString($"wg-contract-category-{category.ToString().ToLowerInvariant()}");

    public static string Money(int amount) => Loc.GetString("wg-contract-thalers", ("amount", amount));

    /// <summary>A span as m:ss, or h:mm:ss over an hour.</summary>
    public static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    public static PanelContainer Background(Color color, Control child)
    {
        var panel = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = color } };
        panel.AddChild(child);
        return panel;
    }

    /// <summary>Plain text in one colour that wraps to its container's width.</summary>
    public static RichTextLabel Wrapped(string text, Color color, Thickness margin)
    {
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();
        var label = new RichTextLabel { Margin = margin };
        label.SetMessage(message);
        return label;
    }

    public static Label Label(string text, Color color, string? style = null)
    {
        var label = new Label { Text = text, FontColorOverride = color, ClipText = true };
        if (style != null)
            label.StyleClasses.Add(style);
        return label;
    }
}

/// <summary>One contract in a list: tier, title, contractor, pay and time.</summary>
public sealed class WGContractRow : ContainerButton
{
    public readonly WGContractView Contract;
    private readonly PanelContainer _panel;
    private readonly Label _time;
    private readonly IGameTiming _timing;

    public WGContractRow(WGContractView contract, IGameTiming timing)
    {
        Contract = contract;
        _timing = timing;
        HorizontalExpand = true;
        Margin = new Thickness(0, 0, 0, 3);

        _panel = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = WGContractStyle.Card } };
        AddChild(_panel);

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(6, 4) };
        _panel.AddChild(row);

        var tier = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = WGContractStyle.TierColor(contract.Tier).WithAlpha(0.25f) },
            MinSize = new Vector2(30, 0),
            Margin = new Thickness(0, 0, 6, 0),
        };
        tier.AddChild(new Label
        {
            Text = Loc.GetString("wg-contract-tier-short", ("tier", contract.Tier)),
            FontColorOverride = WGContractStyle.TierColor(contract.Tier),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        });
        row.AddChild(tier);

        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        var title = WGContractStyle.Label(contract.Title, contract.Locked == null ? WGContractStyle.Bright : WGContractStyle.Dim);
        text.AddChild(title);
        var sub = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        sub.AddChild(WGContractStyle.Label(contract.Contractor, contract.ContractorColor));
        sub.AddChild(WGContractStyle.Label("  ·  " + WGContractStyle.Category(contract.Category), WGContractStyle.Dim));
        if (contract.MinCrew > 1)
            sub.AddChild(WGContractStyle.Label("  ·  " + Loc.GetString("wg-contract-row-crew", ("crew", contract.MinCrew)), WGContractStyle.Warn));
        if (contract.Holder is { } holder)
            sub.AddChild(WGContractStyle.Label("  ·  " + Loc.GetString("wg-contract-row-holder", ("holder", holder)), WGContractStyle.Accent));
        text.AddChild(sub);
        row.AddChild(text);

        var right = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 110 };
        right.AddChild(new Label
        {
            Text = contract.Status is WGContractStatus.Offered or WGContractStatus.Active
                ? WGContractStyle.Money(contract.Reward)
                : Loc.GetString($"wg-contract-status-{contract.Status.ToString().ToLowerInvariant()}"),
            FontColorOverride = contract.Status switch
            {
                WGContractStatus.Completed => WGContractStyle.Good,
                WGContractStatus.Failed or WGContractStatus.Abandoned => WGContractStyle.Bad,
                WGContractStatus.Void => WGContractStyle.Dim,
                _ => WGContractStyle.Accent,
            },
            HorizontalAlignment = HAlignment.Right,
        });
        _time = new Label { FontColorOverride = WGContractStyle.Dim, HorizontalAlignment = HAlignment.Right };
        right.AddChild(_time);
        row.AddChild(right);
        UpdateTime();
    }

    public void SetSelected(bool selected)
    {
        _panel.PanelOverride = new StyleBoxFlat { BackgroundColor = selected ? WGContractStyle.CardSelected : WGContractStyle.Card };
    }

    /// <summary>The time left for a live contract; the time allowed for an offer.</summary>
    public void UpdateTime()
    {
        _time.Text = Contract.Status switch
        {
            WGContractStatus.Active when Contract.Deadline is { } deadline =>
                Loc.GetString("wg-contract-time-left", ("time", WGContractStyle.Clock(deadline - _timing.CurTime))),
            WGContractStatus.Offered => Loc.GetString("wg-contract-time-allowed", ("minutes", (int) Contract.Duration.TotalMinutes)),
            _ => string.Empty,
        };
    }
}

/// <summary>A contract's full details, with one action button underneath.</summary>
public sealed class WGContractDetails : BoxContainer
{
    private readonly IGameTiming _timing;
    private readonly BoxContainer _body;
    private readonly Button _action;
    private readonly Label _note;
    private Label? _clock;
    private WGContractView? _contract;

    public event Action<WGContractView>? OnAction;

    public WGContractDetails(IGameTiming timing)
    {
        _timing = timing;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _body = new BoxContainer { Orientation = LayoutOrientation.Vertical, Margin = new Thickness(8), HorizontalExpand = true };
        scroll.AddChild(_body);
        AddChild(WGContractStyle.Background(WGContractStyle.Panel, scroll));
        ((PanelContainer) Children.Last()).VerticalExpand = true;

        _note = new Label { FontColorOverride = WGContractStyle.Warn, Visible = false, Margin = new Thickness(0, 4, 0, 0) };
        AddChild(_note);
        _action = new Button { Margin = new Thickness(0, 4, 0, 0), MinHeight = 32 };
        _action.OnPressed += _ =>
        {
            if (_contract != null)
                OnAction?.Invoke(_contract);
        };
        AddChild(_action);
        Clear();
    }

    public void Clear()
    {
        _contract = null;
        _clock = null;
        _body.RemoveAllChildren();
        _body.AddChild(new Label
        {
            Text = Loc.GetString("wg-contract-details-none"),
            FontColorOverride = WGContractStyle.Dim,
            HorizontalAlignment = HAlignment.Center,
        });
        _action.Visible = false;
        _note.Visible = false;
    }

    /// <summary>Shows a contract. <paramref name="action"/> labels the button; null hides it.</summary>
    public void Show(WGContractView contract, string? action, string? blocked = null)
    {
        _contract = contract;
        _body.RemoveAllChildren();

        var title = new RichTextLabel();
        title.SetMessage(FormattedMessage.FromMarkupPermissive(Loc.GetString("wg-contract-details-title",
            ("title", FormattedMessage.EscapeText(contract.Title)))));
        _body.AddChild(title);

        var header = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        header.AddChild(WGContractStyle.Label(contract.Contractor, contract.ContractorColor));
        header.AddChild(WGContractStyle.Label("  ·  " + WGContractStyle.Category(contract.Category), WGContractStyle.Dim));
        header.AddChild(WGContractStyle.Label("  ·  " + Loc.GetString("wg-contract-tier", ("tier", contract.Tier)),
            WGContractStyle.TierColor(contract.Tier)));
        _body.AddChild(header);

        _body.AddChild(WGContractStyle.Wrapped(contract.Employer, contract.ContractorColor, new Thickness(0, 0, 0, 6)));

        var briefing = new RichTextLabel { Margin = new Thickness(0, 0, 0, 8) };
        briefing.SetMessage(FormattedMessage.FromMarkupPermissive(contract.Briefing));
        _body.AddChild(briefing);

        _body.AddChild(Section("wg-contract-details-objectives"));
        foreach (var objective in contract.Objectives)
        {
            var line = new RichTextLabel { Margin = new Thickness(6, 0, 0, 0) };
            line.SetMessage(FormattedMessage.FromMarkupPermissive(Loc.GetString("wg-contract-details-objective",
                ("done", objective.Done), ("text", objective.Text))));
            _body.AddChild(line);
        }

        _body.AddChild(Section("wg-contract-details-location"));
        _body.AddChild(WGContractStyle.Wrapped(contract.Location, WGContractStyle.Bright, new Thickness(6, 0, 0, 0)));
        if (contract.Coordinates is { } coords)
        {
            _body.AddChild(WGContractStyle.Wrapped(Loc.GetString("wg-contract-details-coordinates", ("x", (int) coords.X), ("y", (int) coords.Y)),
                WGContractStyle.Accent, new Thickness(6, 0, 0, 0)));
        }

        _body.AddChild(Section("wg-contract-details-terms"));
        _body.AddChild(Term("wg-contract-details-reward", WGContractStyle.Money(contract.Reward), WGContractStyle.Good));
        foreach (var item in contract.Requires)
        {
            _body.AddChild(Term("wg-contract-details-requires", item, WGContractStyle.Warn));
        }
        if (contract.MinCrew > 1)
            _body.AddChild(Term("wg-contract-details-crew", Loc.GetString("wg-contract-row-crew", ("crew", contract.MinCrew)), WGContractStyle.Warn));
        if (contract.CrewBonus is { } bonus)
            _body.AddChild(Term("wg-contract-details-crew-bonus", bonus, WGContractStyle.Good));
        if (contract.Share is { } share)
            _body.AddChild(Term("wg-contract-details-share", share, WGContractStyle.Good));
        if (contract.Credits is { } credits)
            _body.AddChild(Term("wg-contract-details-bonus", credits, WGContractStyle.Good));
        if (contract.Holder is { } holder)
            _body.AddChild(Term("wg-contract-details-holder", holder, WGContractStyle.Accent));
        foreach (var harm in contract.Harms)
        {
            _body.AddChild(Term("wg-contract-details-harms", harm, WGContractStyle.Bad));
        }
        _body.AddChild(Term("wg-contract-details-deposit",
            contract.Deposit > 0 ? WGContractStyle.Money(contract.Deposit) : Loc.GetString("wg-contract-details-deposit-none"),
            contract.Deposit > 0 ? WGContractStyle.Warn : WGContractStyle.Dim));
        _body.AddChild(Term("wg-contract-details-time",
            Loc.GetString("wg-contract-time-allowed", ("minutes", (int) contract.Duration.TotalMinutes)), WGContractStyle.Bright));
        if (contract.Deadline != null)
        {
            var row = Term(contract.Status == WGContractStatus.Active ? "wg-contract-details-deadline" : "wg-contract-details-offer-ends",
                string.Empty, contract.Status == WGContractStatus.Active ? WGContractStyle.Warn : WGContractStyle.Dim, wrap: false);
            _clock = (Label) row.Children.Last();
            _body.AddChild(row);
        }
        else
        {
            _clock = null;
        }

        _note.Text = blocked ?? string.Empty;
        _note.Visible = blocked != null;
        _action.Visible = action != null;
        _action.Text = action ?? string.Empty;
        _action.Disabled = blocked != null;
        UpdateTime();
    }

    public void UpdateTime()
    {
        if (_clock != null && _contract?.Deadline is { } deadline)
            _clock.Text = WGContractStyle.Clock(deadline - _timing.CurTime);
    }

    private static Label Section(string key)
    {
        return new Label
        {
            Text = Loc.GetString(key),
            FontColorOverride = WGContractStyle.Accent,
            Margin = new Thickness(0, 6, 0, 2),
        };
    }

    /// <summary>A labelled value; it wraps unless <paramref name="wrap"/> is false, which keeps it a Label to update in place.</summary>
    private static BoxContainer Term(string key, string value, Color color, bool wrap = true)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        row.AddChild(new Label { Text = Loc.GetString(key), FontColorOverride = WGContractStyle.Dim, MinWidth = 110, VerticalAlignment = VAlignment.Top });
        if (wrap)
        {
            var text = WGContractStyle.Wrapped(value, color, new Thickness(0));
            text.HorizontalExpand = true;
            row.AddChild(text);
        }
        else
        {
            row.AddChild(new Label { Text = value, FontColorOverride = color });
        }

        return row;
    }

}

/// <summary>Standing with each contractor: rank and progress to the next.</summary>
public sealed class WGContractStandingList : BoxContainer
{
    public WGContractStandingList()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
    }

    public void Update(List<WGContractStandingView> standing)
    {
        RemoveAllChildren();
        AddChild(WGContractStyle.Wrapped(Loc.GetString("wg-contract-standing-help"), WGContractStyle.Dim, new Thickness(0, 0, 0, 6)));

        foreach (var entry in standing)
        {
            var card = new BoxContainer { Orientation = LayoutOrientation.Vertical, Margin = new Thickness(6, 4) };
            var top = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            top.AddChild(new Label { Text = entry.Contractor, FontColorOverride = entry.Color, HorizontalExpand = true });
            top.AddChild(new Label { Text = entry.Rank, FontColorOverride = WGContractStyle.Bright });
            card.AddChild(top);
            card.AddChild(WGContractStyle.Wrapped(
                entry.Present ? entry.Description : Loc.GetString("wg-contract-standing-absent", ("description", entry.Description)),
                WGContractStyle.Dim, new Thickness(0)));

            var floor = Math.Min(0, entry.Points);
            var bar = new ProgressBar
            {
                MinValue = entry.Points < 0 ? floor - 1 : 0,
                MaxValue = entry.NextRankAt ?? Math.Max(1, entry.Points),
                Value = entry.Points,
                MinHeight = 6,
                Margin = new Thickness(0, 3, 0, 0),
            };
            card.AddChild(bar);
            card.AddChild(new Label
            {
                Text = entry.NextRankAt is { } next
                    ? Loc.GetString("wg-contract-standing-progress", ("points", entry.Points), ("next", next))
                    : Loc.GetString("wg-contract-standing-max", ("points", entry.Points)),
                FontColorOverride = WGContractStyle.Dim,
            });
            AddChild(WGContractStyle.Background(WGContractStyle.Card, card));
            ((Control) Children.Last()).Margin = new Thickness(0, 0, 0, 4);
        }
    }
}
