using Content.Client._WG.Cockpit;
using Content.Client._WG.Hailing;
using Content.Shared._WG.Hailing;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>The hail window wants the ships in range.</summary>
    public event Action? HailTargetsRequested;

    /// <summary>The pilot hailed a ship.</summary>
    public event Action<NetEntity>? HailRequested;

    /// <summary>The pilot answered or declined an incoming hail.</summary>
    public event Action<bool>? HailAnswerRequested;

    /// <summary>The pilot cancelled their hail or closed the channel.</summary>
    public event Action? HailEndRequested;

    /// <summary>The pilot sent a line down the channel.</summary>
    public event Action<string>? HailSayRequested;

    /// <summary>The pilot picked one of the answers on offer.</summary>
    public event Action<string>? HailChooseRequested;

    /// <summary>The person at a desk offered the caller its own service.</summary>
    public event Action? HailOfferRequested;

    private HailWindow? _hailWindow;
    private HailStrip _hailStrip = default!;
    private EntityUid? _hailConsole;

    private void WgHailInitialize()
    {
        HailButton.OnPressed += _ => WgHailShow();
        HailButton.ChannelOpened += WgHailShow;
        _hailStrip = WgHailCreateStrip();
    }

    /// <summary>
    /// Hands the console its hail window. The interface owns it, so it closes with the console.
    /// </summary>
    public void WgHailAttach(HailWindow window)
    {
        _hailWindow = window;
        window.TargetsRequested += () => HailTargetsRequested?.Invoke();
        window.HailRequested += ship => HailRequested?.Invoke(ship);
        window.AnswerRequested += accept => HailAnswerRequested?.Invoke(accept);
        window.EndRequested += () => HailEndRequested?.Invoke();
        window.SayRequested += text => HailSayRequested?.Invoke(text);
        window.ChooseRequested += id => HailChooseRequested?.Invoke(id);
        window.OfferRequested += () => HailOfferRequested?.Invoke();
    }

    /// <summary>
    /// Puts the hail strip on the caption row of the cockpit's communications housing, and takes it back
    /// out when the cockpit's lease ends.
    /// </summary>
    public void WgHailJoinComms(Control housing, WGCockpitLease lease)
    {
        // The housing is a caption over its content, and nothing else uses the caption's row.
        if (housing.ChildCount == 0 || housing.GetChild(0) is not BoxContainer body || body.ChildCount == 0)
            return;

        // A cockpit torn down without its lease takes the strip with it.
        if (_hailStrip.Disposed)
            _hailStrip = WgHailCreateStrip();

        var strip = _hailStrip;
        var caption = body.GetChild(0);

        var row = new BoxContainer
        {
            Name = "CockpitCommsCaption",
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
        };

        caption.Orphan();
        strip.Orphan();
        row.AddChild(caption);
        row.AddChild(strip);
        body.AddChild(row);
        row.SetPositionInParent(0);
        lease.Remember(strip.Orphan);
    }

    private HailStrip WgHailCreateStrip()
    {
        var strip = new HailStrip();
        strip.AnswerRequested += accept => HailAnswerRequested?.Invoke(accept);
        strip.EndRequested += () => HailEndRequested?.Invoke();
        strip.ViewRequested += WgHailShow;
        strip.SetConsole(_hailConsole);
        return strip;
    }

    private void WgHailShow()
    {
        if (_hailWindow == null || _hailWindow.Disposed)
            return;

        if (_hailWindow.IsOpen)
            _hailWindow.MoveToFront();
        else
            _hailWindow.OpenCenteredRight();
    }

    private void WgHailUpdateState(EntityUid console)
    {
        _hailConsole = console;
        HailButton.SetConsole(console);
        _hailStrip.SetConsole(console);
        _hailWindow?.SetConsole(console);
    }

    /// <summary>Feeds the hail window the ships in range.</summary>
    public void WgHailTargets(List<HailTarget> targets)
    {
        _hailWindow?.UpdateTargets(targets);
    }

    /// <summary>Shows why a hail or call stopped.</summary>
    public void WgHailEnded(HailEndReason reason, string ship)
    {
        HailButton.ShowEnded(reason);
        _hailStrip.ShowEnded(reason, ship);
        _hailWindow?.ShowEnded(reason, ship);
    }
}
