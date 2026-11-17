using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.ShipCommand;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WG.ShipCommand;

/// <summary>
/// The recovery pad console: everyone the pad reaches, the dead and the worst hurt first, each with a recall button. A recall of someone
/// alive and awake takes a second press to confirm. The ship's owner sees who may use the pad, and can change it.
/// </summary>
public sealed class WGRecoveryPadWindow : FancyWindow
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Color Dim = Color.FromHex("#9aa4b1");
    private static readonly Color Warn = Color.FromHex("#e0a33a");

    public event Action<NetEntity, bool>? OnRecall;
    public event Action<WGShipAuthority>? OnAccess;
    public event Action? OnRefresh;

    private static readonly WGShipAuthority[] Levels = { WGShipAuthority.Admin, WGShipAuthority.Officer, WGShipAuthority.Staff };

    private readonly Label _status;
    private readonly BoxContainer _accessRow;
    private readonly OptionButton _access;
    private readonly BoxContainer _list;
    private WGRecoveryPadState? _state;
    private NetEntity? _confirm;

    public WGRecoveryPadWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("wg-recovery-pad-title");
        SetSize = new Vector2(560, 520);
        MinSize = new Vector2(440, 320);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        ContentsContainer.AddChild(root);

        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        _status = new Label { HorizontalExpand = true };
        top.AddChild(_status);
        var refresh = new Button { Text = Loc.GetString("wg-recovery-pad-refresh") };
        refresh.OnPressed += _ => OnRefresh?.Invoke();
        top.AddChild(refresh);
        root.AddChild(top);

        _accessRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Visible = false };
        _accessRow.AddChild(new Label { Text = Loc.GetString("wg-recovery-pad-access"), Margin = new Thickness(0, 0, 8, 0) });
        _access = new OptionButton { MinWidth = 180 };
        for (var i = 0; i < Levels.Length; i++)
            _access.AddItem(Loc.GetString($"wg-recovery-pad-access-{Levels[i].ToString().ToLowerInvariant()}"), i);
        _access.OnItemSelected += args =>
        {
            _access.SelectId(args.Id);
            OnAccess?.Invoke(Levels[args.Id]);
        };
        _accessRow.AddChild(_access);
        root.AddChild(_accessRow);

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        scroll.AddChild(_list);
        root.AddChild(scroll);
    }

    public void UpdateState(WGRecoveryPadState state)
    {
        _state = state;
        _accessRow.Visible = state.HasPad && state.CanConfigure;
        var level = Array.IndexOf(Levels, state.Access);
        if (level >= 0)
            _access.SelectId(level);

        if (_confirm is { } pending && !state.People.Exists(p => p.Entity == pending))
            _confirm = null;

        Rebuild();
        UpdateStatus();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_state is not { } state)
        {
            _status.Text = string.Empty;
            return;
        }

        var now = _timing.CurTime;
        _status.FontColorOverride = Dim;
        if (!state.HasPad)
            _status.Text = Loc.GetString("wg-recovery-pad-status-no-pad");
        else if (!state.Allowed)
            _status.Text = Loc.GetString("wg-recovery-pad-status-not-allowed");
        else if (state.Charging is { } name)
        {
            _status.FontColorOverride = Warn;
            _status.Text = Loc.GetString("wg-recovery-pad-status-charging", ("name", name), ("seconds", Seconds(state.ChargeEnds - now)));
        }
        else if (state.ReadyAt > now)
            _status.Text = Loc.GetString("wg-recovery-pad-status-cooldown", ("seconds", Seconds(state.ReadyAt - now)));
        else
            _status.Text = Loc.GetString("wg-recovery-pad-status-ready");
    }

    private static int Seconds(TimeSpan span) => Math.Max(0, (int) Math.Ceiling(span.TotalSeconds));

    private void Rebuild()
    {
        _list.RemoveAllChildren();
        if (_state is not { HasPad: true, Allowed: true } state)
            return;

        if (state.People.Count == 0)
        {
            _list.AddChild(new Label { Text = Loc.GetString("wg-recovery-pad-nobody"), FontColorOverride = Dim });
            return;
        }

        foreach (var person in state.People)
            _list.AddChild(Row(person, state.Charging == null));
    }

    private Control Row(WGRecoveryPadEntry person, bool idle)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 2) };
        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        text.AddChild(new Label { Text = person.Name });
        text.AddChild(new Label
        {
            Text = Loc.GetString("wg-recovery-pad-row-detail",
                ("condition", Loc.GetString($"wg-recovery-pad-condition-{person.Condition.ToString().ToLowerInvariant()}")),
                ("damage", person.Damage), ("where", person.Where), ("distance", (int) MathF.Round(person.Distance))),
            FontColorOverride = person.Condition == WGRecoveryPadCondition.Alive && person.Damage == 0 ? Dim : Warn,
        });
        row.AddChild(text);

        var confirming = _confirm == person.Entity;
        var button = new Button
        {
            Text = Loc.GetString(confirming ? "wg-recovery-pad-confirm" : "wg-recovery-pad-recall"),
            Disabled = !idle,
            MinWidth = 150,
        };
        if (confirming)
            button.ModulateSelfOverride = Warn;
        button.OnPressed += _ =>
        {
            if (person.Condition == WGRecoveryPadCondition.Alive && !confirming)
            {
                _confirm = person.Entity;
                Rebuild();
                return;
            }

            _confirm = null;
            OnRecall?.Invoke(person.Entity, person.Condition == WGRecoveryPadCondition.Alive);
        };
        row.AddChild(button);
        return row;
    }
}
