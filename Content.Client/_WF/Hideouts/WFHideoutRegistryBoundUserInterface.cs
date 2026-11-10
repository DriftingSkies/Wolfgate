using Content.Shared._WF.Hideouts;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Hideouts;

/// <summary>Opens the outpost registry window and passes its orders to the server.</summary>
public sealed class WFHideoutRegistryBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WFHideoutRegistryWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFHideoutRegistryWindow>();
        _window.OnFound += world => SendMessage(new WFHideoutRegistryFoundMessage(world));
        _window.OnLand += () => SendMessage(new WFHideoutRegistryLandMessage());
        _window.OnRide += () => SendMessage(new WFHideoutRegistryRideMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WFHideoutRegistryState registry)
            _window?.UpdateState(registry);
    }
}
