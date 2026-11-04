using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._WF.MameArcade.NeoGeo;
using Content.Shared._WF.MameArcade;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.MameArcade;

/// <summary>
/// The player's ROM library, the cabinet's 2-player lobby, and the running or watched game. The game on the
/// cabinet's screen streams its sound, and its picture while anyone watches.
/// </summary>
public sealed partial class MameArcadeWindow : DefaultWindow
{
    // How long a 2-player game may wait on the partner before the screen says so.
    private const float StallNotice = 0.5f;

    // A tenth of a second of sound per packet.
    private const int AudioPacketSamples = MameAudioCodec.SampleRate / 10;

    // The picture's frame rate when the rate allows it, and how often a keyframe resets a watcher's screen.
    private const float VideoInterval = 1f / 30f;
    private const float KeyframeSeconds = 10f;

    // How far behind the quickest packet a watcher shows the picture, in ms, so frames that arrive unevenly show evenly.
    private const double WatchBufferMs = 100;

    // How fast that delay follows the network, in ms a second: slow enough that the change in speed goes unseen.
    private const double WatchLagDrift = 30;

    // How fast the quickest trip is forgotten, in ms a second, so the delay recovers after the route slows.
    private const double TripForget = 20;

    // Packets a watcher holds before showing them regardless, about three seconds.
    private const int MaxWatchQueue = 90;

    [Dependency] private IAudioManager _audioManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IFileDialogManager _dialogs = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly MameRomLibrary _library;
    private readonly ISawmill _sawmill;

    private readonly MameArcadeLibraryView _libraryPanel = new();

    private readonly BoxContainer _gamePanel;
    private readonly Label _status;
    private readonly Label _notice;
    private readonly Button _saveButton;
    private readonly Button _loadButton;
    private readonly MameArcadeScreen _screen = new();
    private readonly MameArcadeControlDeck _deck = new();

    private readonly BoxContainer _watchPanel;
    private readonly Label _watchStatus;
    private readonly Button _stopWatching;
    private readonly MameArcadeScreen _watchScreen = new() { CanKeyboardFocus = false, KeyboardFocusOnClick = false };
    private readonly MameArcadeControlDeck _watchDeck = new();
    private MameVideoDecoder? _decoder;
    private string _watchedGame = string.Empty;

    // Picture packets waiting for their moment, and how long after its frame was taken each one shows.
    private readonly Queue<(uint Stamp, byte[] Packet)> _watchQueue = new();
    private double _watchLag = double.NaN;
    private double _quickestTrip;

    private readonly List<short[]> _audioScratch = new();
    private readonly List<(int Frame, uint Checksum)> _checksumScratch = new();
    private readonly Queue<(int Frame, uint Checksum)> _checksumsToSend = new();

    // What this client sends while its game is on the cabinet.
    private readonly List<short> _stereoOut = new();
    private readonly List<short> _monoOut = new();
    private MameAudioEncoder _audioEncoder = new();
    private MameVideoEncoder _encoder = new();
    private Task<byte[]>? _encoding;
    private bool _encodingKeyframe;

    // A typical change packet, so frames go out evenly spaced when the rate can't take every one.
    private float _videoPacketBytes;
    private float _videoAllowance;
    private float _sinceVideo;
    private float _sinceKeyframe;
    private int _joinsSeen;

    // Inputs can beat the start message here, which waits for its game tick; kept until the game starts.
    private readonly List<(int First, ushort[] Inputs, int ChecksumFrame, uint Checksum, int Margin, int Delay)> _early = new();
    private const int MaxEarlyMessages = 256;

    private MameArcadeSession? _session;
    private MameArcadeAudio? _audio;
    private MameArcadeBuiState? _lobby;
    private string _hostedGame = string.Empty;
    private string _partnerName = string.Empty;
    private int _lastFrame;
    private float _stalled;

    public event Action<string, string>? HostRequested;
    public event Action<string>? JoinRequested;
    public event Action? LeaveRequested;
    /// <summary>First frame, inputs, checksum frame, checksum, this side's slack and delay, for the partner.</summary>
    public event Action<int, ushort[], int, uint, int, int>? InputsReady;

    /// <summary>Started a 1-player game, by set name.</summary>
    public event Action<string>? SoloStarted;

    /// <summary>Starts or stops watching the cabinet's game.</summary>
    public event Action<bool>? WatchRequested;

    /// <summary>A packet of this client's game for the people around the cabinet.</summary>
    public event Action<MameArcadeStreamKind, byte[]>? StreamReady;

    public MameArcadeWindow()
    {
        IoCManager.InjectDependencies(this);
        _sawmill = _log.GetSawmill("mame.arcade");
        _library = new MameRomLibrary(_resources.UserData);

        Title = Loc.GetString("mame-arcade-title");
        SetSize = new Vector2(900, 760);
        MinSize = new Vector2(640, 520);

        _libraryPanel.ImportPressed += Import;
        _libraryPanel.FolderPressed += _library.OpenFolder;
        _libraryPanel.RefreshPressed += RefreshLibrary;
        _libraryPanel.PlayPressed += PlaySolo;
        _libraryPanel.HostPressed += Host;
        _libraryPanel.JoinPressed += Join;
        _libraryPanel.CancelPressed += () => LeaveRequested?.Invoke();
        _libraryPanel.WatchPressed += Watch;

        _status = new Label { HorizontalExpand = true };
        _saveButton = new Button { Text = Loc.GetString("mame-arcade-save-state") };
        _saveButton.OnPressed += _ => _session?.RequestSaveState();
        _loadButton = new Button { Text = Loc.GetString("mame-arcade-load-state") };
        _loadButton.OnPressed += _ => LoadState();
        var quit = new Button { Text = Loc.GetString("mame-arcade-quit") };
        quit.OnPressed += _ => Quit();
        _notice = new Label { FontColorOverride = Color.Gray };

        _gamePanel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            Visible = false,
            SeparationOverride = 4,
            Children =
            {
                new BoxContainer { Children = { _status, _saveButton, _loadButton, quit } },
                _screen,
                _notice,
                _deck,
            },
        };

        _watchStatus = new Label { HorizontalExpand = true };
        _stopWatching = new Button { Text = Loc.GetString("mame-arcade-stop-watching") };
        _stopWatching.OnPressed += _ => StopWatching(null);

        _watchPanel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            Visible = false,
            SeparationOverride = 4,
            Children =
            {
                new BoxContainer { Children = { _watchStatus, _stopWatching } },
                _watchScreen,
                _watchDeck,
            },
        };

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children = { _libraryPanel, _gamePanel, _watchPanel },
        });

        RefreshLibrary();
        UpdateLobby();
    }

    /// <summary>
    /// Whether the cabinet ignores this player's buttons, such as a ghost's. The server puts them in the audience
    /// itself, and the window only ever shows the game on the screen.
    /// </summary>
    public bool Spectator { get; private set; }

    public void SetSpectator()
    {
        Spectator = true;
        _libraryPanel.Visible = false;
        _watchPanel.Visible = true;
        _stopWatching.Visible = false;
        _watchStatus.Text = Loc.GetString("mame-arcade-spectator-idle");
    }

    /// <summary>The cabinet's lobby changed.</summary>
    public void SetLobby(MameArcadeBuiState state)
    {
        _lobby = state;
        UpdateLobby();

        if (_decoder != null && (state.Showing.Player == null || state.Showing.Game != _watchedGame))
            StopWatching(Loc.GetString("mame-arcade-watch-ended"));

        if (Spectator && _decoder == null && state.Showing.Player != null)
            Watch();

        // The server refused this game because someone else took the cabinet first.
        var local = _entMan.GetNetEntity(_player.LocalEntity);
        if (_session is { Netplay: null } && (state.Host != null || state.Showing.Player is { } player && player != local))
            StopSession(Loc.GetString("mame-arcade-cabinet-busy"));
    }

    /// <summary>Whether the window shows another player's game.</summary>
    public bool Watching => _decoder != null;

    /// <summary>The player has the set and BIOS the lobby's host plays with.</summary>
    private bool CanJoin(MameArcadeBuiState lobby)
    {
        return MameGames.Find(lobby.Game) is { } set
            && MameGames.Fingerprint(set, _library.Snapshot()) == lobby.Fingerprint
            && MameGames.Missing(set, _library.Snapshot()).Count == 0;
    }

    /// <summary>A picture packet of the watched game.</summary>
    public void ReceiveVideo(byte[] packet)
    {
        if (_decoder == null || !MameVideoCodec.TryReadStamp(packet, out var stamp))
            return;

        var trip = Trip(stamp);
        if (double.IsNaN(_watchLag))
        {
            _watchLag = trip + WatchBufferMs;
            _quickestTrip = trip;
        }

        _quickestTrip = Math.Min(_quickestTrip, trip);
        _watchQueue.Enqueue((stamp, packet));
    }

    /// <summary>
    /// Shows the watched packets whose moment has come. The delay trails the quickest recent packet by a fixed buffer
    /// and drifts toward it, so the picture keeps the player's pace however the packets bunch up on the way.
    /// </summary>
    private void ShowWatched(float delta)
    {
        if (_decoder == null || double.IsNaN(_watchLag))
            return;

        _quickestTrip += delta * TripForget;
        var drift = delta * WatchLagDrift;
        _watchLag = Math.Clamp(_quickestTrip + WatchBufferMs, _watchLag - drift, _watchLag + drift);

        var shown = false;
        while (_watchQueue.TryPeek(out var next) && (Trip(next.Stamp) >= _watchLag || _watchQueue.Count > MaxWatchQueue))
        {
            _watchQueue.Dequeue();
            shown |= _decoder.Decode(next.Packet);
        }

        if (!shown)
            return;

        _watchScreen.SetFrame(_decoder.Frame, _decoder.Width, _decoder.Height);
        _watchStatus.Text = WatchingText();
        UpdateWatchDeck(_decoder.Deck);
    }

    /// <summary>Milliseconds from the player's clock at <paramref name="stamp"/> to this client's clock now.</summary>
    private double Trip(uint stamp) => unchecked((int) (Now() - stamp));

    private uint Now() => (uint) _timing.RealTime.TotalMilliseconds;

    private void ResetWatchClock()
    {
        _watchQueue.Clear();
        _watchLag = double.NaN;
    }

    /// <summary>The server seated both players; start from power-on.</summary>
    public void StartTwoPlayer(MameArcadeStartMessage message)
    {
        var game = message.Seat == 0 ? _hostedGame : _lobby?.Game ?? string.Empty;
        if (MameGames.Find(game) is not { } set)
        {
            LeaveRequested?.Invoke();
            return;
        }

        _partnerName = message.PartnerName;
        var early = _early.ToList();
        StartSession(set, new MameNetplay(message.Seat, message.Delay));
        foreach (var (first, inputs, checksumFrame, checksum, margin, delay) in early)
        {
            ReceiveInputs(first, inputs, checksumFrame, checksum, margin, delay);
        }
    }

    public void ReceiveInputs(int firstFrame, ushort[] inputs, int checksumFrame, uint checksum, int margin, int delay)
    {
        if (_session?.Netplay is not { } netplay)
        {
            if (_early.Count < MaxEarlyMessages)
                _early.Add((firstFrame, inputs, checksumFrame, checksum, margin, delay));
            return;
        }

        netplay.ReceiveInputs(firstFrame, inputs, margin, delay);
        if (checksumFrame >= 0 && netplay.AddRemoteChecksum(checksumFrame, checksum))
            Desynced();
    }

    public void PartnerLeft()
    {
        if (_session?.Netplay is not { Alone: false } netplay)
            return;

        netplay.PartnerLeft();
        _notice.Text = Loc.GetString("mame-arcade-partner-left", ("seat", 2 - netplay.Seat));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        ShowWatched(args.DeltaSeconds);

        if (_session == null)
            return;

        switch (_session.State)
        {
            case MameSessionState.Loading:
                _status.Text = Loc.GetString("mame-arcade-loading", ("game", _session.Set.Title));
                return;
            case MameSessionState.Failed:
                _sawmill.Error($"MAME arcade stopped: {_session.Error}");
                LeaveRequested?.Invoke();
                StopSession(Loc.GetString("mame-arcade-failed"));
                return;
            case MameSessionState.Stopped:
                LeaveRequested?.Invoke();
                StopSession(null);
                return;
        }

        _session.Update(args.DeltaSeconds, _screen.Held);
        _session.Netplay?.Adapt(args.DeltaSeconds);

        var showing = IsShowing();
        var newFrame = _session.TryTakeFrame(out var pixels);
        if (newFrame)
            _screen.SetFrame(pixels, _session.Width, _session.Height);

        UpdateVideo(args.DeltaSeconds, showing, newFrame ? pixels : null);

        _session.TakeAudio(_audioScratch);
        foreach (var samples in _audioScratch)
        {
            _audio?.Feed(samples);
            if (showing)
                _stereoOut.AddRange(samples);
        }

        _audioScratch.Clear();
        if (_audio != null)
        {
            _audio.Gain = _cfg.GetCVar(CCVars.InterfaceVolume);
            _audio.Update();
        }

        SendAudio(showing);

        if (_session.TryTakeSavedState(out var state))
        {
            _library.SaveState(_session.Set.Name, state);
            _notice.Text = Loc.GetString("mame-arcade-state-saved");
        }

        if (_session.Netplay is { } netplay)
            UpdateNetplay(netplay, args.DeltaSeconds);
        else
            _status.Text = _session.Set.Title;

        if (showing && _lobby?.Showing.Watchers is > 0 and var watchers)
            _status.Text = Loc.GetString("mame-arcade-status-watched", ("status", _status.Text ?? string.Empty), ("count", watchers));

        UpdateDeck();
    }

    /// <summary>The local seat shows the keys as pressed, the partner's what their last frame used.</summary>
    private void UpdateDeck()
    {
        var held = _screen.Held;
        if (_session?.Netplay is not { } netplay)
        {
            _deck.SetSeat(0, held, string.Empty, local: true, active: true);
            _deck.SetSeat(1, MameControls.None, string.Empty, local: false, active: false);
            return;
        }

        var (one, two) = _session.LastControls;
        var partner = 1 - netplay.Seat;
        _deck.SetSeat(netplay.Seat, held, string.Empty, local: true, active: true);
        _deck.SetSeat(partner, partner == 0 ? one : two, _partnerName, local: false, active: !netplay.Alone);
    }

    private void UpdateWatchDeck(MameDeckState deck)
    {
        var lobby = _lobby;
        var twoPlayer = lobby?.Host != null && lobby.Guest != null;
        var one = twoPlayer ? lobby!.HostName : lobby?.Showing.PlayerName ?? string.Empty;
        var two = twoPlayer ? lobby!.GuestName : lobby?.Showing.PlayerName ?? string.Empty;
        _watchDeck.SetSeat(0, deck.PlayerOne, one, local: false, active: deck.OneActive);
        _watchDeck.SetSeat(1, deck.PlayerTwo, two, local: false, active: deck.TwoActive);
    }

    /// <summary>What the watchers' decks show: the seats in play and their controls in the frame sent.</summary>
    private MameDeckState DeckForWatchers()
    {
        if (_session == null)
            return default;

        var (one, two) = _session.LastControls;
        if (_session.Netplay is not { } netplay)
            return new MameDeckState(one, two, true, false);

        var partnerPlays = !netplay.Alone;
        return new MameDeckState(one, two, netplay.Seat == 0 || partnerPlays, netplay.Seat == 1 || partnerPlays);
    }

    public override void Close()
    {
        StopSession(null);
        base.Close();
    }

    /// <summary>
    /// Stops the game and any watching without telling the server, for when the server closed the cabinet first,
    /// such as when the player walks away. The window is then disposed without <see cref="Close"/>.
    /// </summary>
    public void Shutdown()
    {
        StopSession(null);
        _decoder = null;
        _watchedGame = string.Empty;
        _watchScreen.Clear();
        ResetWatchClock();
    }

    /// <summary>This client's game is the one on the cabinet.</summary>
    private bool IsShowing()
    {
        return _session != null
            && _lobby?.Showing.Player is { } player
            && player == _entMan.GetNetEntity(_player.LocalEntity);
    }

    /// <summary>Sends the game's sound a tenth of a second at a time, for the people near the cabinet.</summary>
    private void SendAudio(bool showing)
    {
        if (!showing)
        {
            _stereoOut.Clear();
            _monoOut.Clear();
            return;
        }

        _audioEncoder.Downmix(_stereoOut, _monoOut);
        while (_monoOut.Count >= AudioPacketSamples)
        {
            StreamReady?.Invoke(MameArcadeStreamKind.Audio, _audioEncoder.Encode(_monoOut.GetRange(0, AudioPacketSamples)));
            _monoOut.RemoveRange(0, AudioPacketSamples);
        }
    }

    /// <summary>
    /// While anyone watches, encodes a frame on a worker whenever the server's rate allows, so a busy screen comes
    /// through at a lower frame rate rather than late. Frames go out evenly spaced at the rate a typical packet allows,
    /// rather than in bursts whenever the allowance refills.
    /// </summary>
    private void UpdateVideo(float delta, bool showing, Rgba32[]? pixels)
    {
        if (_encoding is { IsCompleted: true } done)
        {
            _encoding = null;
#pragma warning disable RA0004 // IsCompleted was checked above, so this can't block.
            if (done.IsCompletedSuccessfully && showing)
            {
                if (!_encodingKeyframe)
                {
                    _videoPacketBytes = _videoPacketBytes == 0f
                        ? done.Result.Length
                        : MathHelper.Lerp(_videoPacketBytes, done.Result.Length, 0.2f);
                }

                _videoAllowance -= done.Result.Length;
                StreamReady?.Invoke(MameArcadeStreamKind.Video, done.Result);
            }
#pragma warning restore RA0004
        }

        if (!showing || _lobby?.Showing is not { Watchers: > 0, VideoRate: > 0 } audience)
        {
            _videoAllowance = 0f;
            _sinceKeyframe = KeyframeSeconds;
            return;
        }

        _videoAllowance = MathF.Min(_videoAllowance + audience.VideoRate * delta, audience.VideoRate);
        _sinceVideo += delta;
        _sinceKeyframe += delta;

        var interval = MathF.Max(VideoInterval, _videoPacketBytes / audience.VideoRate);
        if (pixels == null || _encoding != null || _videoAllowance <= 0f || _sinceVideo < interval)
            return;

        var keyframe = _sinceKeyframe >= KeyframeSeconds || audience.Joins != _joinsSeen;
        _joinsSeen = audience.Joins;
        if (keyframe)
            _sinceKeyframe = 0f;

        // Keeps the remainder, so frames that land just short of the interval don't slip to every other one.
        _sinceVideo = MathF.Min(_sinceVideo - interval, interval);
        _encodingKeyframe = keyframe;
        var (width, height, encoder, deck, stamp) = (_session!.Width, _session.Height, _encoder, DeckForWatchers(), Now());
        var frame = encoder.Stage(pixels);
        _encoding = Task.Run(() => encoder.Encode(frame, width, height, keyframe, deck, stamp));
    }

    private void Watch()
    {
        if (_lobby?.Showing is not { Player: not null } showing || _session != null)
            return;

        _decoder = new MameVideoDecoder();
        ResetWatchClock();
        _watchedGame = showing.Game;
        _watchScreen.Clear();
        _watchDeck.Board = MameGames.Find(showing.Game)?.Board ?? MameBoard.NeoGeo;
        UpdateWatchDeck(default);
        _watchStatus.Text = Loc.GetString("mame-arcade-watch-waiting");
        _libraryPanel.Visible = false;
        _watchPanel.Visible = true;

        // The server already counts a spectator in, and would drop their message.
        if (!Spectator)
            WatchRequested?.Invoke(true);
    }

    private void StopWatching(string? message)
    {
        if (_decoder == null)
            return;

        _decoder = null;
        ResetWatchClock();
        _watchedGame = string.Empty;
        _watchScreen.Clear();

        if (Spectator)
        {
            _watchStatus.Text = Loc.GetString("mame-arcade-spectator-idle");
            return;
        }

        _watchPanel.Visible = false;
        _libraryPanel.Visible = true;
        _libraryPanel.SetMessage(message);

        WatchRequested?.Invoke(false);
        UpdateLobby();
    }

    private string WatchingText()
    {
        var showing = _lobby?.Showing;
        var title = MameGames.Find(_watchedGame)?.Title ?? _watchedGame;
        return Loc.GetString("mame-arcade-watching", ("player", showing?.PlayerName ?? string.Empty), ("game", title));
    }

    private void UpdateNetplay(MameNetplay netplay, float delta)
    {
        _session!.TakeChecksums(_checksumScratch);
        foreach (var (frame, checksum) in _checksumScratch)
        {
            if (netplay.AddLocalChecksum(frame, checksum))
            {
                Desynced();
                return;
            }

            _checksumsToSend.Enqueue((frame, checksum));
        }

        _checksumScratch.Clear();

        if (!netplay.Alone && netplay.TakeOutbox(out var first, out var inputs))
        {
            for (var start = 0; start < inputs.Length; start += MameArcadeProtocol.MaxInputsPerMessage)
            {
                var chunk = inputs.Skip(start).Take(MameArcadeProtocol.MaxInputsPerMessage).ToArray();
                var (checksumFrame, checksum) = _checksumsToSend.TryDequeue(out var pending)
                    ? pending
                    : (MameArcadeProtocol.NoChecksum, 0u);
                InputsReady?.Invoke(first + start, chunk, checksumFrame, checksum, netplay.Margin, netplay.Delay);
            }
        }

        _stalled = netplay.Frame == _lastFrame ? _stalled + delta : 0f;
        _lastFrame = netplay.Frame;

        _status.Text = !netplay.Alone && _stalled > StallNotice
            ? Loc.GetString("mame-arcade-waiting-partner")
            : Loc.GetString("mame-arcade-seat",
                ("game", _session.Set.Title),
                ("seat", netplay.Seat + 1),
                ("partner", _partnerName),
                ("delay", (int) Math.Round(netplay.Delay * _session.FrameSeconds * 1000)));
    }

    private void RefreshLibrary()
    {
        _library.Refresh();
        _libraryPanel.SetLibrary(_library.Snapshot(), _library.ZipCount, _library.UnusedZips(MameGames.IsKnownChip), _library.HasState);
        UpdateLobby();
    }

    private void UpdateLobby()
    {
        var local = _entMan.GetNetEntity(_player.LocalEntity);
        var lobby = _lobby;
        UpdateShowing(lobby?.Showing, local);

        if (lobby?.Host is not { } host)
        {
            var otherPlaying = lobby?.Showing.Player is { } player && player != local;
            _libraryPanel.SetLobby(null, false, false, busy: otherPlaying);
            return;
        }

        var title = MameGames.Find(lobby.Game)?.Title ?? lobby.Game;
        if (lobby.Guest != null)
        {
            var text = Loc.GetString("mame-arcade-lobby-busy", ("host", lobby.HostName), ("guest", lobby.GuestName), ("game", title));
            _libraryPanel.SetLobby(text, false, false, busy: true);
        }
        else if (host == local)
        {
            _libraryPanel.SetLobby(Loc.GetString("mame-arcade-lobby-waiting", ("game", title)), false, true, busy: true);
        }
        else
        {
            var canJoin = CanJoin(lobby);
            var text = canJoin
                ? Loc.GetString("mame-arcade-lobby-hosting", ("host", lobby.HostName), ("game", title))
                : Loc.GetString("mame-arcade-lobby-need-game", ("host", lobby.HostName), ("game", title));
            _libraryPanel.SetLobby(text, canJoin && _session == null, false, busy: true);
        }
    }

    /// <summary>Offers to watch another player's game on the cabinet.</summary>
    private void UpdateShowing(MameArcadeShowing? showing, NetEntity? local)
    {
        if (showing?.Player is not { } player || player == local || _session != null)
        {
            _libraryPanel.SetShowing(null, false, false);
            return;
        }

        var title = MameGames.Find(showing.Game)?.Title ?? showing.Game;
        var text = Loc.GetString("mame-arcade-showing", ("player", showing.PlayerName), ("game", title));
        _libraryPanel.SetShowing(text, showing.VideoRate > 0, showing.Watchers >= showing.MaxWatchers);
    }

    private async void Import()
    {
        var (stream, name) = await _dialogs.GetFileAndName(
            new FileDialogFilters(new FileDialogFilters.Group("zip")),
            FileAccess.Read);

        if (stream == null || name == null)
            return;

        await using (stream)
        {
            _libraryPanel.SetMessage(_library.Import(name, stream) ? null : Loc.GetString("mame-arcade-import-failed"));
        }

        RefreshLibrary();
    }

    private void PlaySolo()
    {
        if (_libraryPanel.Selected is not { } set)
            return;

        StartSession(set, null);
        SoloStarted?.Invoke(set.Name);
    }

    private void Host()
    {
        if (_libraryPanel.Selected is not { } set)
            return;

        var fingerprint = MameGames.Fingerprint(set, _library.Snapshot());
        if (fingerprint.Length == 0)
            return;

        _hostedGame = set.Name;
        HostRequested?.Invoke(set.Name, fingerprint);
    }

    private void Join()
    {
        if (_lobby == null || MameGames.Find(_lobby.Game) is not { } set)
            return;

        JoinRequested?.Invoke(MameGames.Fingerprint(set, _library.Snapshot()));
    }

    private void LoadState()
    {
        if (_session == null)
            return;

        if (_library.TryLoadState(_session.Set.Name, out var state))
            _session.LoadState(state);
        else
            _notice.Text = Loc.GetString("mame-arcade-no-state");
    }

    private void StartSession(IMameGame set, MameNetplay? netplay)
    {
        StopSession(null);
        StopWatching(null);

        _session = new MameArcadeSession(set, _library.Snapshot(), _library.Nvram(), netplay);
        _audio = new MameArcadeAudio(_audioManager);
        _audioEncoder = new MameAudioEncoder();
        _encoder = new MameVideoEncoder();
        _videoPacketBytes = 0f;
        _lastFrame = 0;
        _stalled = 0f;
        _checksumsToSend.Clear();

        var solo = netplay == null;
        _saveButton.Visible = solo;
        _loadButton.Visible = solo;
        _notice.Text = string.Empty;
        _libraryPanel.SetMessage(null);
        _libraryPanel.Visible = false;
        _gamePanel.Visible = true;
        _deck.Board = set.Board;
        _screen.GrabKeyboardFocus();
    }

    private void Quit()
    {
        LeaveRequested?.Invoke();
        StopSession(null);
    }

    private void Desynced()
    {
        if (_session?.Netplay is not { } netplay)
            return;

        netplay.PartnerLeft();
        LeaveRequested?.Invoke();
        _notice.Text = Loc.GetString("mame-arcade-desynced");
    }

    private void StopSession(string? message)
    {
        _session?.Dispose();
        _session = null;
        _audio?.Dispose();
        _audio = null;
        _screen.Clear();
        _hostedGame = string.Empty;
        _early.Clear();
        _stereoOut.Clear();
        _monoOut.Clear();
        _encoding = null;

        _gamePanel.Visible = false;
        _libraryPanel.Visible = true;
        if (message != null)
            _libraryPanel.SetMessage(message);

        UpdateLobby();
    }
}
