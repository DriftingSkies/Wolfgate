using Content.Shared._WF.Outposts;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Outposts;

/// <summary>Opens the planet broadcast window and sends what is typed in it, or a call for help.</summary>
[UsedImplicitly]
public sealed class WFPlanetBroadcastBoundUserInterface : BoundUserInterface
{
    private WFPlanetBroadcastWindow? _window;

    public WFPlanetBroadcastBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFPlanetBroadcastWindow>();
        _window.OnSend += (text, position) => SendMessage(new WFPlanetBroadcastMessage(text, position));
        _window.OnDistress += () => SendMessage(new WFPlanetDistressMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WFPlanetBroadcastState broadcast)
            _window?.UpdateState(broadcast);
    }
}
