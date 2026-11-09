using System.Text;
using Content.Shared._WF.JoinProgress;
using Robust.Server.Network.Transfer;
using Robust.Server.Upload;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server._WF.JoinProgress;

/// <summary>Adds up uploaded content and tells each joining client the size of its download.</summary>
public sealed partial class JoinProgressManager
{
    // Each file's two lengths and continue byte, see SharedNetworkResourceManager.WriteFileStream.
    private const int PerFileOverhead = 9;

    // The ack key at the start of the stream.
    private const int StreamOverhead = 4;

    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private NetworkResourceManager _netRes = default!;
    [Dependency] private IServerTransferStatus _transferStatus = default!;

    private readonly Dictionary<ResPath, long> _fileSizes = new();
    private long _totalBytes;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgJoinDownloadInfo>();
        _net.RegisterNetMessage<MsgJoinTransferStatus>();
        _net.Connected += OnConnected;
        _netRes.ResourcesUploaded += ev => TrackFiles(ev.Files);
    }

    /// <summary>Counts uploaded files towards the download size; a path uploaded again replaces its old size.</summary>
    public void TrackFiles(IEnumerable<(ResPath Relative, byte[] Data)> files)
    {
        foreach (var (path, data) in files)
        {
            var size = data.Length + Encoding.UTF8.GetByteCount(path.CanonPath) + PerFileOverhead;
            _totalBytes += size - _fileSizes.GetValueOrDefault(path);
            _fileSizes[path] = size;
        }
    }

    private void OnConnected(object? sender, NetChannelArgs args)
    {
        // The engine decided the channel during the handshake, falling back when the websocket couldn't connect.
        var mode = _transferStatus.GetMode(args.Channel.UserId) switch
        {
            TransferChannelMode.WebSocket => JoinTransferMode.WebSocket,
            TransferChannelMode.WebSocketFailed => JoinTransferMode.WebSocketFailed,
            _ => JoinTransferMode.GameConnection,
        };

        _net.ServerSendMessage(new MsgJoinTransferStatus { Mode = mode }, args.Channel);

        if (_fileSizes.Count == 0)
            return;

        _net.ServerSendMessage(new MsgJoinDownloadInfo
        {
            TotalBytes = _totalBytes + StreamOverhead,
            FileCount = _fileSizes.Count,
            OverGameConnection = mode != JoinTransferMode.WebSocket,
        }, args.Channel);
    }
}
