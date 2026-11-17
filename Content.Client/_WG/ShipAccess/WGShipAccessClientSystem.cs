using Content.Shared._WG.ShipAccess;

namespace Content.Client._WG.ShipAccess;

/// <summary>
/// Client side of the codes: opens the keypad when the server asks after the Enter Code verb, sends the
/// entered code back, and hands the owner's codes and keypad alerts to the console's access tab.
/// </summary>
public sealed class WGShipAccessClientSystem : EntitySystem
{
    private ShipAccessKeypadWindow? _keypad;

    /// <summary>The server sent this owner the ship's codes.</summary>
    public event Action<WGShipAccessCodesEvent>? CodesReceived;

    /// <summary>The server reported a keypad miss on a ship this owner owns.</summary>
    public event Action<WGShipAccessCodeAlertEvent>? AlertReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGShipAccessOpenKeypadEvent>(OnOpenKeypad);
        SubscribeNetworkEvent<WGShipAccessCodesEvent>(ev => CodesReceived?.Invoke(ev));
        SubscribeNetworkEvent<WGShipAccessCodeAlertEvent>(ev => AlertReceived?.Invoke(ev));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _keypad?.Close();
    }

    private void OnOpenKeypad(WGShipAccessOpenKeypadEvent ev)
    {
        _keypad?.Close();
        var door = ev.Door;
        var keypad = new ShipAccessKeypadWindow();
        keypad.Submitted += code => RaiseNetworkEvent(new WGShipAccessSubmitCodeMessage(door, code));
        keypad.OnClose += () =>
        {
            if (_keypad == keypad)
                _keypad = null;
        };
        _keypad = keypad;
        keypad.OpenCentered();
    }
}
