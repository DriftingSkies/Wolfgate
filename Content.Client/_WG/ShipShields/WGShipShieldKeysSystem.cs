using Content.Shared._WG.ShipShields;
using Robust.Shared.Input.Binding;

namespace Content.Client._WG.ShipShields;

/// <summary>Sends the pilot's shield and vent hotkeys to the server, which checks the helm and acts on the ship.</summary>
public sealed partial class WGShipShieldKeysSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        // The keys only exist in the shuttle context, and the server checks the sender is piloting.
        CommandBinds.Builder
            .Bind(WGShipShieldKeys.WGToggleShipShields, InputCmdHandler.FromDelegate(_ => RaiseNetworkEvent(new WGShipShieldToggleEvent()),
                handle: true, outsidePrediction: true))
            .Bind(WGShipShieldKeys.WGVentShipHeat, InputCmdHandler.FromDelegate(_ => RaiseNetworkEvent(new WGShipVentKeyEvent()),
                handle: true, outsidePrediction: true))
            .Register<WGShipShieldKeysSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGShipShieldKeysSystem>();
    }
}
