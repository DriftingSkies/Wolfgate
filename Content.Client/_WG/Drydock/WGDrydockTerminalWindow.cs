using System.Globalization;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.Drydock;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Drydock;

/// <summary>
/// The drydock terminal: a row for each linked berth (its ship, what it is doing, its lock, and a button that opens the
/// berth's own console window), the lock-all buttons, and on a commanded ship the drydock price. Rows are rebuilt from
/// each state; the price box keeps what was typed.
/// </summary>
public sealed class WGDrydockTerminalWindow : FancyWindow
{
    private static readonly Color Dim = Color.FromHex("#9aa4b1");
    private static readonly Color Warn = Color.FromHex("#e0a33a");

    /// <summary>Raised with a message for the terminal: a refresh, a lock, or the price.</summary>
    public event Action<BoundUserInterfaceMessage>? OnSend;

    /// <summary>Raised with a berth console when its row's open button is pressed.</summary>
    public event Action<NetEntity>? OnOpenBerth;

    private readonly Label _header;
    private readonly BoxContainer _berths;
    private readonly BoxContainer _lockAll;
    private readonly Button _lockAllButton;
    private readonly Button _unlockAllButton;
    private readonly BoxContainer _price;
    private readonly Label _priceNow;
    private readonly LineEdit _priceBox;
    private readonly Button _priceSet;
    private readonly Button _priceClear;
    private readonly RichTextLabel _footer;

    /// <summary>Builds the window's header, berth list, lock-all row, price row and footer.</summary>
    public WGDrydockTerminalWindow()
    {
        Title = Loc.GetString("drydock-terminal-title");
        SetSize = new Vector2(720, 460);
        MinSize = new Vector2(560, 320);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        ContentsContainer.AddChild(root);

        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        _header = new Label { HorizontalExpand = true };
        top.AddChild(_header);
        top.AddChild(Btn("drydock-terminal-refresh", () => OnSend?.Invoke(new WGDrydockTerminalRefreshMessage())));
        root.AddChild(top);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _berths = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_berths);
        root.AddChild(scroll);

        _lockAll = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _lockAll.AddChild(new Label { Text = Loc.GetString("drydock-terminal-all-berths"), HorizontalExpand = true });
        _lockAllButton = Btn("drydock-terminal-lock-all", () => OnSend?.Invoke(new WGDrydockTerminalLockMessage(null, true)));
        _unlockAllButton = Btn("drydock-terminal-unlock-all", () => OnSend?.Invoke(new WGDrydockTerminalLockMessage(null, false)));
        _lockAll.AddChild(_lockAllButton);
        _lockAll.AddChild(_unlockAllButton);
        root.AddChild(_lockAll);

        _price = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _priceNow = new Label { HorizontalExpand = true };
        _priceBox = new LineEdit { MinWidth = 70, PlaceHolder = Loc.GetString("drydock-terminal-price-placeholder") };
        _priceSet = Btn("drydock-terminal-price-set", SetPrice);
        _priceClear = Btn("drydock-terminal-price-clear", () => OnSend?.Invoke(new WGDrydockTerminalPriceMessage(null)));
        _priceBox.OnTextEntered += _ => SetPrice();
        _price.AddChild(_priceNow);
        _price.AddChild(_priceBox);
        _price.AddChild(_priceSet);
        _price.AddChild(_priceClear);
        root.AddChild(_price);

        _footer = new RichTextLabel { Margin = new Thickness(0, 6, 0, 0) };
        root.AddChild(_footer);
    }

    private static Button Btn(string key, Action act)
    {
        var button = new Button { Text = Loc.GetString(key), Margin = new Thickness(4, 0, 0, 0) };
        button.OnPressed += _ => act();
        return button;
    }

    private void SetPrice()
    {
        if (!float.TryParse(_priceBox.Text.Trim().TrimStart('x', '×'), NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier))
            return;

        OnSend?.Invoke(new WGDrydockTerminalPriceMessage(multiplier));
    }

    /// <summary>Rebuilds the berth rows and shows what this viewer may do, from the state sent to them.</summary>
    public void UpdateState(WGDrydockTerminalState state)
    {
        _header.Text = Loc.GetString("drydock-terminal-header", ("count", state.Berths.Count));

        _berths.RemoveAllChildren();
        if (state.Berths.Count == 0)
            _berths.AddChild(new Label { Text = Loc.GetString("drydock-terminal-no-berths"), FontColorOverride = Dim });

        foreach (var berth in state.Berths)
            _berths.AddChild(Row(state, berth));

        _lockAll.Visible = state.CanLock && state.Berths.Count > 0;
        _lockAllButton.Disabled = state.Berths.TrueForAll(b => b.Locked);
        _unlockAllButton.Disabled = state.Berths.TrueForAll(b => !b.Locked);

        _price.Visible = state.Commanded && state.CanConfigure;
        _priceNow.Text = state.Multiplier is { } m
            ? Loc.GetString("drydock-terminal-price-now", ("multiplier", m.ToString("0.##", CultureInfo.InvariantCulture)))
            : Loc.GetString("drydock-terminal-price-usual");
        _priceClear.Disabled = state.Multiplier == null;

        _footer.SetMessage(Loc.GetString(!state.CanOperate
            ? "drydock-terminal-footer-not-cleared"
            : state.Commanded
                ? "drydock-terminal-footer-commanded"
                : "drydock-terminal-footer"), Dim);
    }

    private Control Row(WGDrydockTerminalState state, WGDrydockTerminalBerth berth)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 2) };
        row.AddChild(new Label { Text = berth.Label, MinWidth = 110 });

        var (status, warn) = Status(berth.State);
        row.AddChild(new Label
        {
            Text = status,
            HorizontalExpand = true,
            ClipText = true,
            FontColorOverride = warn ? Warn : null,
        });

        if (berth.Locked)
            row.AddChild(new Label { Text = Loc.GetString("drydock-terminal-locked"), FontColorOverride = Warn, Margin = new Thickness(6, 0) });

        if (state.CanLock)
        {
            var net = berth.Console;
            var locked = berth.Locked;
            row.AddChild(Btn(locked ? "drydock-terminal-unlock" : "drydock-terminal-lock",
                () => OnSend?.Invoke(new WGDrydockTerminalLockMessage(net, !locked))));
        }

        var console = berth.Console;
        var open = Btn("drydock-terminal-open", () => OnOpenBerth?.Invoke(console));
        open.Disabled = !state.CanOperate;
        row.AddChild(open);
        return row;
    }

    /// <summary>One line for what a berth is doing, and whether it wants attention.</summary>
    private static (string Text, bool Warn) Status(WGDrydockConsoleState s)
    {
        if (s.CraneCount == 0)
            return (Loc.GetString("drydock-terminal-status-no-cranes"), true);

        if (s.Working)
        {
            var key = s.Breaking ? "drydock-terminal-status-breaking" : "drydock-terminal-status-working";
            return (Loc.GetString(key,
                ("ship", s.ShipName ?? Loc.GetString("drydock-terminal-unknown-ship")),
                ("percent", (int) MathF.Round(s.Progress * 100)),
                ("minutes", (int) MathF.Ceiling(s.RemainingSeconds / 60f))), false);
        }

        if (s.ShipName == null)
            return (Loc.GetString("drydock-terminal-status-empty"), false);

        var work = s.MissingTiles + s.MissingParts + s.DamagedParts;
        return work > 0
            ? (Loc.GetString("drydock-terminal-status-berthed", ("ship", s.ShipName), ("work", work)), false)
            : (Loc.GetString("drydock-terminal-status-sound", ("ship", s.ShipName)), false);
    }
}
