using Content.Shared._WF.Fighters;
using Content.Shared.Shuttles.BUIStates;
using JetBrains.Annotations;

namespace Content.Client._WF.Fighters.UI;

/// <summary>Opens the fighter's flight deck for its pilot and closes it when the interface closes.</summary>
[UsedImplicitly]
public sealed class FighterCockpitBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FighterCockpitView? _view;

    protected override void Open()
    {
        base.Open();
        if (_view != null)
            return;

        _view = FighterCockpitView.TryCreate(Owner, Close,
            () => SendMessage(new FighterCockpitFollowCursorMessage()),
            () => SendMessage(new FighterCockpitLeaveMessage()),
            mode => SendMessage(new FighterCockpitFlightModeMessage(mode)));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is NavBoundUserInterfaceState nav)
            _view?.UpdateState(nav.State);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _view?.Restore();
        _view = null;
    }
}
