using Content.Client.UserInterface;
using Content.Shared._WF.ExtraInteractions;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;

namespace Content.Client._WF.ExtraInteractions;

/// <summary>
/// Turns a point at a carried item's hand, inventory or storage slot into a show request. The point key doesn't
/// focus the HUD, so otherwise it falls through to whatever lies in the world under the slot.
/// </summary>
public sealed class WFShowItemUIController : UIController
{
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();
        _input.UIKeyBindStateChanged += OnUIKeyBind;
    }

    private bool OnUIKeyBind(BoundKeyEventArgs args)
    {
        if (args.Function != ContentKeyFunctions.Point || args.State != BoundKeyState.Down || args.Handled)
            return false;

        if (_player.LocalEntity is not { } user || FindEntity(UIManager.MouseGetControl(args.PointerLocation)) is not { } item)
            return false;

        if (!EntityManager.System<WFShowItemSystem>().IsCarried(user, item))
            return false;

        EntityManager.RaisePredictiveEvent(new WFShowItemRequestEvent(EntityManager.GetNetEntity(item)));
        args.Handle();
        return true;
    }

    /// <summary>The entity a HUD control stands for, from it or the nearest parent that has one.</summary>
    private static EntityUid? FindEntity(Control? control)
    {
        for (; control != null && control is not IViewportControl; control = control.Parent)
        {
            if (control is IEntityControl { UiEntity: { } entity })
                return entity;
        }

        return null;
    }
}
