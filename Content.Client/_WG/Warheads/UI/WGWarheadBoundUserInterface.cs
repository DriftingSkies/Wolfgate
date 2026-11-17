using Content.Shared._WG.Warheads;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Warheads.UI;

/// <summary>Opens the warhead control panel.</summary>
[UsedImplicitly]
public sealed class WGWarheadBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WGWarheadWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGWarheadWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnSetTimer += seconds => SendMessage(new WGWarheadSetTimerMessage(seconds));
        _window.OnArm += () => SendMessage(new WGWarheadArmMessage());
        _window.OnTurnKey += () => SendMessage(new WGWarheadTurnKeyMessage());
        _window.OnSafety += safe => SendMessage(new WGWarheadSafetyMessage(safe));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WGWarheadBoundUserInterfaceState warhead)
            _window?.UpdateState(warhead);
    }
}
