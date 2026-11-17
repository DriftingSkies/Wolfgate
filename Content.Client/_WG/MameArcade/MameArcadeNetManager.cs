using Content.Shared._WG.MameArcade;
using Robust.Shared.Network;

namespace Content.Client._WG.MameArcade;

/// <summary>Sends and receives 2-player inputs and the cabinet streams as soon as they arrive, outside the game tick.</summary>
public sealed partial class MameArcadeNetManager
{
    [Dependency] private IClientNetManager _net = default!;

    public event Action<MsgMameArcadeInput>? InputReceived;
    public event Action<MsgMameArcadeStream>? StreamReceived;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgMameArcadeInput>(msg => InputReceived?.Invoke(msg));
        _net.RegisterNetMessage<MsgMameArcadeStream>(msg => StreamReceived?.Invoke(msg));
    }

    public void Send(NetMessage message)
    {
        _net.ClientSendMessage(message);
    }
}
