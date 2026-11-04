using Content.Shared._WF.MameArcade;
using Content.Shared.ActionBlocker;
using Robust.Client.UserInterface;

namespace Content.Client._WF.MameArcade;

/// <summary>Opens the cabinet's window and carries its lobby, 2-player inputs and streams to and from the server.</summary>
public sealed partial class MameArcadeBoundUserInterface : BoundUserInterface
{
    [Dependency] private MameArcadeNetManager _net = default!;

    private MameArcadeWindow? _window;

    public MameArcadeBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<MameArcadeWindow>();
        if (PlayerManager.LocalEntity is not { } local || !EntMan.System<ActionBlockerSystem>().CanInteract(local, Owner))
            _window.SetSpectator();

        _window.HostRequested += (game, fingerprint) => SendMessage(new MameArcadeHostMessage(game, fingerprint));
        _window.JoinRequested += fingerprint => SendMessage(new MameArcadeJoinMessage(fingerprint));
        _window.LeaveRequested += () => SendMessage(new MameArcadeLeaveMessage());
        _window.InputsReady += SendInputs;
        _window.SoloStarted += game => SendMessage(new MameArcadeSoloMessage(game));
        _window.WatchRequested += watching => SendMessage(new MameArcadeWatchMessage(watching));
        _window.StreamReady += SendStream;
        _net.InputReceived += OnInputReceived;
        _net.StreamReceived += OnStreamReceived;
    }

    protected override void Dispose(bool disposing)
    {
        // Disposing the window skips its Close, which would leave the game's worker holding the emulator.
        if (disposing)
            _window?.Shutdown();

        base.Dispose(disposing);
        _net.InputReceived -= OnInputReceived;
        _net.StreamReceived -= OnStreamReceived;
    }

    private void SendStream(MameArcadeStreamKind kind, byte[] data)
    {
        _net.Send(new MsgMameArcadeStream
        {
            Cabinet = EntMan.GetNetEntity(Owner),
            Kind = kind,
            Data = data,
        });
    }

    private void OnStreamReceived(MsgMameArcadeStream message)
    {
        if (message.Kind == MameArcadeStreamKind.Video && message.Cabinet == EntMan.GetNetEntity(Owner))
            _window?.ReceiveVideo(message.Data);
    }

    private void SendInputs(int firstFrame, ushort[] inputs, int checksumFrame, uint checksum, int margin, int delay)
    {
        _net.Send(new MsgMameArcadeInput
        {
            Cabinet = EntMan.GetNetEntity(Owner),
            FirstFrame = firstFrame,
            Inputs = inputs,
            ChecksumFrame = checksumFrame,
            Checksum = checksum,
            Margin = (sbyte) Math.Clamp(margin, sbyte.MinValue, sbyte.MaxValue),
            Delay = (byte) Math.Clamp(delay, 0, byte.MaxValue),
        });
    }

    private void OnInputReceived(MsgMameArcadeInput message)
    {
        if (message.Cabinet == EntMan.GetNetEntity(Owner))
            _window?.ReceiveInputs(message.FirstFrame, message.Inputs, message.ChecksumFrame, message.Checksum, message.Margin, message.Delay);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is MameArcadeBuiState lobby)
            _window?.SetLobby(lobby);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        switch (message)
        {
            case MameArcadeStartMessage start:
                _window?.StartTwoPlayer(start);
                break;
            case MameArcadePartnerLeftMessage:
                _window?.PartnerLeft();
                break;
        }
    }
}
