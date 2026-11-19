using Content.Shared._WG.Cooking;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Cooking;

/// <summary>Opens the prep board window and sends its buttons and chopping results to the server.</summary>
public sealed class WGPrepBoardBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WGPrepBoardWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WGPrepBoardWindow>();
        _window.OnAssemble += () => SendMessage(new WGPrepBoardAssembleMessage());
        _window.OnTake += item => SendMessage(new WGPrepBoardTakeMessage(item));
        _window.OnChop += (item, score, option) => SendMessage(new WGPrepBoardChopMessage(item, score, option));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WGPrepBoardState board)
            _window?.Update(board, EntMan);
    }
}
