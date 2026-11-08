using System.Numerics;
using Content.Shared._WF.Outposts;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Outposts;

/// <summary>
/// A line to type an announcement into, the world it goes to and how long until the console can send again; under
/// raid, a button to call the whole sector for help.
/// </summary>
public sealed class WFPlanetBroadcastWindow : DefaultWindow
{
    [Dependency] private IGameTiming _timing = default!;

    public event Action<string, bool>? OnSend;
    public event Action? OnDistress;

    private readonly Label _world;
    private readonly Label _wait;
    private readonly LineEdit _text;
    private readonly Button _send;
    private readonly CheckBox _position;
    private readonly BoxContainer _distressRow;
    private readonly Label _distressLabel;
    private readonly Button _distress;
    private TimeSpan _readyAt;
    private TimeSpan _distressAt;
    private int _maxLength = 200;

    public WFPlanetBroadcastWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("wf-outpost-broadcast-title");
        MinSize = new Vector2(420, 180);

        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(6),
        };
        Contents.AddChild(column);

        _world = new Label();
        _wait = new Label { StyleClasses = { "LabelSubText" } };
        _text = new LineEdit { HorizontalExpand = true, PlaceHolder = Loc.GetString("wf-outpost-broadcast-placeholder") };
        _send = new Button { Text = Loc.GetString("wf-outpost-broadcast-send") };
        _text.OnTextEntered += _ => Send();
        _send.OnPressed += _ => Send();
        _position = new CheckBox { Text = Loc.GetString("wf-outpost-broadcast-include-position") };

        _distressLabel = new Label { HorizontalExpand = true, StyleClasses = { "LabelSubText" } };
        _distress = new Button { Text = Loc.GetString("wf-outpost-distress-send"), StyleClasses = { "Caution" } };
        _distress.OnPressed += _ => OnDistress?.Invoke();
        _distressRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Visible = false,
        };
        _distressRow.AddChild(_distressLabel);
        _distressRow.AddChild(_distress);

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        row.AddChild(_text);
        row.AddChild(_send);
        column.AddChild(_world);
        column.AddChild(row);
        column.AddChild(_position);
        column.AddChild(_wait);
        column.AddChild(_distressRow);
    }

    public void UpdateState(WFPlanetBroadcastState state)
    {
        _world.Text = state.World.Length == 0
            ? Loc.GetString("wf-outpost-broadcast-no-world")
            : Loc.GetString("wf-outpost-broadcast-world", ("world", state.World));
        _maxLength = state.MaxLength;
        _readyAt = _timing.CurTime + TimeSpan.FromSeconds(state.WaitSeconds);
        _distressAt = _timing.CurTime + TimeSpan.FromSeconds(state.DistressWaitSeconds);
        _distressRow.Visible = state.Raided;
        UpdateWait();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateWait();
    }

    private void UpdateWait()
    {
        var seconds = (int) Math.Ceiling((_readyAt - _timing.CurTime).TotalSeconds);
        _send.Disabled = seconds > 0;
        _wait.Text = seconds > 0
            ? Loc.GetString("wf-outpost-broadcast-wait", ("seconds", seconds))
            : Loc.GetString("wf-outpost-broadcast-limit", ("length", _maxLength));

        if (!_distressRow.Visible)
            return;

        var distress = (int) Math.Ceiling((_distressAt - _timing.CurTime).TotalSeconds);
        _distress.Disabled = distress > 0;
        _distressLabel.Text = distress > 0
            ? Loc.GetString("wf-outpost-distress-wait", ("seconds", distress))
            : Loc.GetString("wf-outpost-distress-ready");
    }

    private void Send()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0 || text.Length > _maxLength || _send.Disabled)
            return;

        OnSend?.Invoke(text, _position.Pressed);
        _text.Clear();
    }
}
