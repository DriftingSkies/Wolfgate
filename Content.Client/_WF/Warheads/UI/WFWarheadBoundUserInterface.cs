using Content.Shared._WF.Warheads;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Warheads.UI;

/// <summary>Opens the warhead control panel.</summary>
[UsedImplicitly]
public sealed class WFWarheadBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WFWarheadWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFWarheadWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnSetTimer += seconds => SendMessage(new WFWarheadSetTimerMessage(seconds));
        _window.OnArm += () => SendMessage(new WFWarheadArmMessage());
        _window.OnTurnKey += () => SendMessage(new WFWarheadTurnKeyMessage());
        _window.OnSafety += safe => SendMessage(new WFWarheadSafetyMessage(safe));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WFWarheadBoundUserInterfaceState warhead)
            _window?.UpdateState(warhead);
    }
}
