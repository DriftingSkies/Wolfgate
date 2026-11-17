using System.Numerics;
using Content.Shared._WG.Pool;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._WG.Pool;

/// <summary>The pool table's window: a card for each side, the table, and what just happened under it.</summary>
public sealed class PoolWindow : DefaultWindow
{
    private static readonly Color TurnColor = Color.FromHex("#E8C020");
    private static readonly Color CardColor = Color.FromHex("#1D1D21");
    private static readonly Color CardEdge = Color.FromHex("#3A3A42");

    private readonly IEntityManager _entMan;
    private readonly IPlayerManager _player;
    private readonly WGPoolTableSystem _tables;

    private readonly PoolTableControl _table = new();
    private readonly StyleBoxFlat[] _cards = { MakeCardStyle(), MakeCardStyle() };
    private readonly Label[] _names = { new(), new() };
    private readonly Label[] _groups = { new(), new() };
    private readonly PoolBallRow[] _rows = { new(), new() { Mirrored = true } };
    private readonly Button[] _seatButtons = { new(), new() };
    private readonly Label _status = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _hint = new() { HorizontalExpand = true, ClipText = true, VerticalAlignment = VAlignment.Center };
    private readonly Button _rerack = new() { Text = Loc.GetString("wg-pool-rerack") };

    private PoolUiState? _state;
    private int? _shotId;

    public event Action<float, float>? OnShoot;
    public event Action<Vector2>? OnPlace;
    public event Action<int>? OnSeat;
    public event Action? OnRerack;

    public PoolWindow()
    {
        _entMan = IoCManager.Resolve<IEntityManager>();
        _player = IoCManager.Resolve<IPlayerManager>();
        _tables = _entMan.System<WGPoolTableSystem>();

        Title = Loc.GetString("wg-pool-window-title");
        Resizable = false;

        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        header.AddChild(MakeSide(0));
        header.AddChild(MakeSide(1));

        _status.StyleClasses.Add("LabelHeading");
        _hint.StyleClasses.Add("LabelSubText");
        var footer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        footer.AddChild(_hint);
        footer.AddChild(_rerack);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        root.AddChild(header);
        root.AddChild(_table);
        root.AddChild(_status);
        root.AddChild(footer);
        Contents.AddChild(root);

        _table.OnShoot += (angle, power) => OnShoot?.Invoke(angle, power);
        _table.OnPlace += at => OnPlace?.Invoke(at);
        _table.OnSettled += Refresh;
        _rerack.OnPressed += _ => OnRerack?.Invoke();
    }

    private static StyleBoxFlat MakeCardStyle()
    {
        var style = new StyleBoxFlat { BackgroundColor = CardColor, BorderColor = CardEdge, BorderThickness = new Thickness(2) };
        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, 5);
        return style;
    }

    /// <summary>A side's card: the player's name over their balls, and the button to join or leave. The right one is mirrored.</summary>
    private PanelContainer MakeSide(int side)
    {
        var right = side == 1;
        var lower = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MinHeight = 20 };
        _groups[side].StyleClasses.Add("LabelSubText");
        _groups[side].HorizontalExpand = true;
        _rows[side].HorizontalExpand = true;
        lower.AddChild(_groups[side]);
        lower.AddChild(_rows[side]);

        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        _names[side].ClipText = true;
        text.AddChild(_names[side]);
        text.AddChild(lower);

        _seatButtons[side].VerticalAlignment = VAlignment.Center;
        _seatButtons[side].OnPressed += _ => OnSeat?.Invoke(MySeat() == side ? -1 : side);

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        if (right)
        {
            _names[side].Align = Label.AlignMode.Right;
            _groups[side].Align = Label.AlignMode.Right;
            box.AddChild(_seatButtons[side]);
            box.AddChild(text);
        }
        else
        {
            box.AddChild(text);
            box.AddChild(_seatButtons[side]);
        }

        var card = new PanelContainer { PanelOverride = _cards[side], HorizontalExpand = true };
        card.AddChild(box);
        return card;
    }

    public void UpdateState(PoolUiState state)
    {
        // The first state is the table as it stands; after that a new shot id is a shot to replay.
        var replay = _shotId != null && _shotId != state.ShotId && state.ShotId > 0 && state.ShotStart.Length == PoolRules.BallCount;
        var racked = _shotId > state.ShotId;
        _state = state;
        _shotId = state.ShotId;

        if (state.Balls.Length != PoolRules.BallCount)
            return;

        if (racked)
            _table.Rerack();

        if (replay)
            _table.Replay(state.ShotStart, state.ShotStartDown, state.ShotVelocity, state.Balls, state.Down);
        else
            _table.Show(state.Balls, state.Down);

        Refresh();
    }

    private int? MySeat()
    {
        if (_state == null || _player.LocalEntity is not { } local || !_entMan.TryGetNetEntity(local, out var net))
            return null;

        for (var side = 0; side < _state.Seats.Length; side++)
        {
            if (_state.Seats[side] == net)
                return side;
        }

        return null;
    }

    private string SideName(int side)
    {
        return _state != null && side < _state.SeatNames.Length && _state.SeatNames[side] is { } name
            ? name
            : Loc.GetString("wg-pool-side", ("number", side + 1));
    }

    /// <summary>Whether the local player may shoot: seated, their turn or alone at the table, and holding a cue.</summary>
    private bool MayShoot(PoolUiState state, int? seat, out bool needsCue)
    {
        needsCue = false;
        if (state.Winner >= 0 || seat == null || state.Turn != seat && state.Seats[state.Turn] != null)
            return false;

        needsCue = _player.LocalEntity is not { } local || !_tables.HoldsCue(local);
        return !needsCue;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_state is not { } state || state.Seats.Length < 2)
            return;

        // Checked every frame: the cue can leave the player's hand at any time.
        var seat = MySeat();
        var mayShoot = MayShoot(state, seat, out var needsCue);
        _table.CanShoot = mayShoot;
        _table.CanPlace = mayShoot && state.BallInHand;
        _table.Kitchen = state.Breaking;

        string hint;
        if (seat == null)
            hint = "wg-pool-hint-spectator";
        else if (state.Winner >= 0)
            hint = "wg-pool-hint-over";
        else if (needsCue)
            hint = "wg-pool-hint-cue";
        else if (!mayShoot)
            hint = "wg-pool-hint-waiting";
        else if (state.BallInHand)
            hint = state.Breaking ? "wg-pool-hint-break" : "wg-pool-hint-ball-in-hand";
        else
            hint = "wg-pool-hint-shoot";

        _hint.Text = Loc.GetString(hint);
    }

    /// <summary>Fills in the cards and the status line. Both wait for a replay to end, so they can't give the shot away.</summary>
    private void Refresh()
    {
        if (_state is not { } state || state.Seats.Length < 2 || state.Groups.Length < 2)
            return;

        var seat = MySeat();
        for (var side = 0; side < 2; side++)
        {
            _names[side].Text = SideName(side);
            _seatButtons[side].Text = Loc.GetString(seat == side ? "wg-pool-leave" : "wg-pool-join");
            _seatButtons[side].Visible = seat == side || seat == null && state.Seats[side] == null;
        }

        _rerack.Disabled = seat == null || _table.Animating;

        if (_table.Animating)
        {
            _status.Text = " ";
            return;
        }

        for (var side = 0; side < 2; side++)
        {
            var shooting = state.Winner < 0 ? state.Turn == side : state.Winner == side;
            _names[side].FontColorOverride = shooting ? TurnColor : null;
            _cards[side].BorderColor = shooting ? TurnColor : CardEdge;

            var open = state.Groups[side] == PoolGroup.None;
            _groups[side].Visible = open;
            _groups[side].Text = Loc.GetString("wg-pool-group-open");
            _rows[side].Visible = !open;
            _rows[side].Group = state.Groups[side];
            _rows[side].Down = state.Down;
        }

        var player = state.Winner >= 0 ? state.Winner : state.Turn;
        _status.Text = Loc.GetString($"wg-pool-outcome-{state.Outcome.ToString().ToLowerInvariant()}",
            ("player", SideName(player)), ("other", SideName(1 - player)));
    }
}
