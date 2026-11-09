using Content.Client._WF.JoinProgress;
using Content.Shared._WF.JoinProgress;
using Robust.Client;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Launcher;

public sealed partial class LauncherConnecting
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private JoinProgressManager _joinProgress = default!;

    private long _downloadBaselineBytes;

    public JoinProgressTracker JoinTracker { get; } = new();

    public TimeSpan JoinNow => _timing.RealTime;

    public MsgJoinDownloadInfo? JoinDownloadInfo => _joinProgress.DownloadInfo;

    /// <summary>Which channel this connection's file transfers use; null until the server says.</summary>
    public JoinTransferMode? JoinTransferMode => _joinProgress.TransferMode;

    public bool CanReconnectJoin => _gameController.LaunchState.ConnectEndpoint != null;

    /// <summary>Round trip to the server in ms, or null with no connection yet.</summary>
    public int? JoinPing => _clientNetManager.ServerChannel?.Ping;

    public NetworkStats JoinNetStats => _clientNetManager.Statistics;

    /// <summary>Bytes received this stage, or null when the game connection isn't what carries the files.</summary>
    public long? JoinDownloadedBytes
    {
        get
        {
            if (JoinTracker.Stage != JoinStage.Downloading || JoinDownloadInfo is not { OverGameConnection: true })
                return null;

            return Math.Max(0, _clientNetManager.Statistics.ReceivedBytes - _downloadBaselineBytes);
        }
    }

    private void StartJoinProgress()
    {
        _clientNetManager.ClientConnectStateChanged += OnJoinConnectState;
        _clientNetManager.Connected += OnJoinNetConnected;
        _baseClient.RunLevelChanged += OnJoinRunLevelChanged;
        _joinProgress.DownloadInfoReceived += OnJoinDownloadInfo;
        _joinProgress.TransferModeReceived += OnJoinTransferMode;
        ConnectFailed += OnJoinConnectFailed;

        OnJoinConnectState(_clientNetManager.ClientConnectState);
    }

    private void StopJoinProgress()
    {
        _clientNetManager.ClientConnectStateChanged -= OnJoinConnectState;
        _clientNetManager.Connected -= OnJoinNetConnected;
        _baseClient.RunLevelChanged -= OnJoinRunLevelChanged;
        _joinProgress.DownloadInfoReceived -= OnJoinDownloadInfo;
        _joinProgress.TransferModeReceived -= OnJoinTransferMode;
        ConnectFailed -= OnJoinConnectFailed;
    }

    private void OnJoinTransferMode(JoinTransferMode mode)
    {
        if (mode == Content.Shared._WF.JoinProgress.JoinTransferMode.GameConnection)
            return;

        JoinTracker.AddLog(Loc.GetString($"join-console-log-channel-{mode}"), JoinNow);
    }

    /// <summary>Drops a hung connection and starts over. The engine has no other way out of a stuck handshake.</summary>
    public void ReconnectJoin()
    {
        if (_clientNetManager.ClientConnectState != ClientConnectionState.NotConnecting)
        {
            JoinTracker.AddLog(Loc.GetString("join-console-log-reconnect"), JoinNow);
            _baseClient.DisconnectFromServer(Loc.GetString("join-progress-reconnect-reason"));
        }

        RetryConnect();
    }

    private void OnJoinConnectState(ClientConnectionState state)
    {
        switch (state)
        {
            case ClientConnectionState.NotConnecting:
                if (JoinTracker.Stage != JoinStage.None)
                    JoinTracker.AddLog(Loc.GetString("join-console-log-closed"), JoinNow);

                JoinTracker.Reset(JoinNow);
                break;
            case ClientConnectionState.Connected:
                JoinTracker.Advance(JoinStage.Handshake, JoinNow);
                JoinTracker.AddLog(Loc.GetString("join-console-log-Handshake"), JoinNow);
                break;
            default:
                JoinTracker.Advance(JoinStage.Connecting, JoinNow);
                JoinTracker.AddLog(Loc.GetString($"connecting-state-{state}").ToUpperInvariant(), JoinNow);
                break;
        }
    }

    private void OnJoinNetConnected(object? sender, NetChannelArgs args)
    {
        _downloadBaselineBytes = _clientNetManager.Statistics.ReceivedBytes;
        JoinTracker.Advance(JoinStage.Downloading, JoinNow);
        JoinTracker.AddLog(Loc.GetString("join-console-log-Downloading"), JoinNow);
    }

    private void OnJoinRunLevelChanged(object? sender, RunLevelChangedEventArgs args)
    {
        if (args.NewLevel == ClientRunLevel.Initialize)
        {
            JoinTracker.Reset(JoinNow);
        }
        else if (args.NewLevel >= ClientRunLevel.Connected && JoinTracker.Stage < JoinStage.EnteringGame)
        {
            JoinTracker.Advance(JoinStage.EnteringGame, JoinNow);
            JoinTracker.AddLog(Loc.GetString("join-console-log-EnteringGame"), JoinNow);
        }
    }

    private void OnJoinDownloadInfo(MsgJoinDownloadInfo info)
    {
        JoinTracker.AddLog(Loc.GetString("join-console-log-files",
            ("count", info.FileCount),
            ("size", ByteHelpers.FormatBytes(info.TotalBytes).ToUpperInvariant())), JoinNow);
    }

    private void OnJoinConnectFailed(NetConnectFailArgs args)
    {
        JoinTracker.AddLog(Loc.GetString("join-console-log-failed"), JoinNow);
    }
}
