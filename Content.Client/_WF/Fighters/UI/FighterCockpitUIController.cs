using Content.Client.UserInterface.Systems.Gameplay;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._WF.Fighters.UI;

/// <summary>Hands the HUD back from an open fighter deck before the game screen it borrowed from unloads.</summary>
public sealed class FighterCockpitUIController : UIController
{
    private FighterCockpitView? _active;

    public override void Initialize()
    {
        base.Initialize();
        UIManager.GetUIController<GameplayStateLoadController>().OnScreenUnload += Release;
    }

    public void Track(FighterCockpitView view)
    {
        _active = view;
    }

    public void Release()
    {
        var view = _active;
        _active = null;
        view?.Restore();
    }

    public void Forget(FighterCockpitView view)
    {
        if (_active == view)
            _active = null;
    }
}
