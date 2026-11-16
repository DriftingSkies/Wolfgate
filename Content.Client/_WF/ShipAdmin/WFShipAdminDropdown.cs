using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WF.ShipAdmin;

/// <summary>
/// The dropdown every Shipmaster list uses: a search box over the options, the picked option stays picked, and a
/// click on it while it is open closes it. The engine's own reopens, as the click that closes the list also opens it.
/// </summary>
public sealed class WFShipAdminDropdown : OptionButton
{
    private readonly IGameTiming _timing;
    private readonly Popup? _popup;
    private uint _hiddenFrame = uint.MaxValue;
    private bool _closedByThisClick;

    public WFShipAdminDropdown()
    {
        _timing = IoCManager.Resolve<IGameTiming>();
        Filterable = true;
        OnItemSelected += args => SelectId(args.Id);

        // Subscribed after the engine's own handler, which has opened the list again by the time this runs.
        OnPressed += _ =>
        {
            if (_closedByThisClick)
                _popup?.Close();
        };

        _popup = OptionsScroll.Parent as Popup;
        if (_popup != null)
            _popup.OnPopupHide += () => _hiddenFrame = _timing.CurFrame;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        // A press outside an open list closes it before the press is handed on, in the same frame.
        if (args.Function == EngineKeyFunctions.UIClick)
            _closedByThisClick = _hiddenFrame == _timing.CurFrame;

        base.KeyBindDown(args);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick)
            _closedByThisClick = false;
    }
}
