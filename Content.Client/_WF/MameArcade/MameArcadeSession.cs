using System.Threading;
using System.Threading.Tasks;
using Content.Client._WF.MameArcade.Engine;
using Content.Client._WF.MameArcade.NeoGeo;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.MameArcade;

public enum MameSessionState : byte
{
    Loading,
    Running,
    Failed,
    Stopped,
}

/// <summary>
/// One running game. The emulator runs on a worker, a frame at a time as the main thread asks for them, so a slow
/// frame never stalls the client. Without <see cref="Netplay"/>, player 2 is never pressed.
/// </summary>
public sealed class MameArcadeSession : IDisposable
{
    // The engine is static: each session's worker waits for the previous one to finish.
    private static Task _lastWorker = Task.CompletedTask;
    private static readonly object WorkerChain = new();

    // Frames handed to the worker but not yet run; enough to ride out a hitch without adding lag.
    private const int MaxQueuedFrames = 3;

    // Stops a long client hitch from turning into a burst of game time.
    private const int MaxFramesPerUpdate = 4;

    private readonly object _lock = new();
    private readonly Queue<FrameJob> _jobs = new();
    private readonly List<short[]> _audioOut = new();
    private readonly List<(int Frame, uint Checksum)> _checksums = new();
    private readonly IMameGame _set;
    private readonly IMameChipSource _chips;
    private readonly INvramStore? _nvram;
    private Rgba32[] _front = Array.Empty<Rgba32>();
    private Rgba32[] _back = Array.Empty<Rgba32>();
    private bool _frameReady;
    private bool _stopping;
    private byte[]? _stateToLoad;
    private bool _saveRequested;
    private byte[]? _savedState;
    private double _accumulator;
    private double _frameSeconds = 1 / 60.0;

    public MameSessionState State { get; private set; } = MameSessionState.Loading;
    public string? Error { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public IMameGame Set => _set;

    /// <summary>Seconds per emulated frame, once loaded.</summary>
    public double FrameSeconds => _frameSeconds;

    /// <summary>Set for a 2-player game.</summary>
    public readonly MameNetplay? Netplay;

    /// <summary>Both seats' controls in the last frame handed to the emulator.</summary>
    public (MameControls PlayerOne, MameControls PlayerTwo) LastControls { get; private set; }

    /// <param name="nvram">Where battery RAM persists. Leave null for 2-player games, which must start identical.</param>
    public MameArcadeSession(IMameGame set, IMameChipSource chips, INvramStore? nvram, MameNetplay? netplay)
    {
        _set = set;
        _chips = chips;
        _nvram = netplay == null ? nvram : null;
        Netplay = netplay;

        lock (WorkerChain)
        {
            var previous = _lastWorker;
            _lastWorker = Task.Run(async () =>
            {
                await previous;
                Run();
            });
        }
    }

    /// <summary>Advances by real time with the local player's controls held.</summary>
    public void Update(float deltaSeconds, MameControls held)
    {
        if (State != MameSessionState.Running)
            return;

        _accumulator = Math.Min(_accumulator + deltaSeconds, MaxFramesPerUpdate * _frameSeconds);

        lock (_lock)
        {
            while (_accumulator >= _frameSeconds && _jobs.Count < MaxQueuedFrames)
            {
                MameControls playerOne, playerTwo;
                int frame;
                if (Netplay != null)
                {
                    // Spend a due frame without running it, to fall a frame behind a partner that waits on us.
                    if (Netplay.TakeHold())
                    {
                        _accumulator -= _frameSeconds;
                        continue;
                    }

                    frame = Netplay.Frame;
                    if (!Netplay.TryAdvance(held, out playerOne, out playerTwo))
                        break;
                }
                else
                {
                    frame = -1;
                    playerOne = held;
                    playerTwo = MameControls.None;
                }

                _jobs.Enqueue(new FrameJob(frame, playerOne, playerTwo));
                LastControls = (playerOne, playerTwo);
                _accumulator -= _frameSeconds;
            }

            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>The newest frame, if one finished since the last call.</summary>
    public bool TryTakeFrame(out Rgba32[] pixels)
    {
        lock (_lock)
        {
            pixels = _front;
            if (!_frameReady)
                return false;

            _frameReady = false;
            return true;
        }
    }

    /// <summary>Sound produced since the last call, as interleaved stereo.</summary>
    public void TakeAudio(List<short[]> into)
    {
        lock (_lock)
        {
            into.AddRange(_audioOut);
            _audioOut.Clear();
        }
    }

    /// <summary>Checksums of finished frames, for comparing with the partner.</summary>
    public void TakeChecksums(List<(int Frame, uint Checksum)> into)
    {
        lock (_lock)
        {
            into.AddRange(_checksums);
            _checksums.Clear();
        }
    }

    /// <summary>Asks for a save state; it arrives through <see cref="TryTakeSavedState"/>.</summary>
    public void RequestSaveState()
    {
        lock (_lock)
        {
            _saveRequested = true;
            Monitor.PulseAll(_lock);
        }
    }

    public bool TryTakeSavedState(out byte[] state)
    {
        lock (_lock)
        {
            state = _savedState ?? Array.Empty<byte>();
            _savedState = null;
            return state.Length > 0;
        }
    }

    public void LoadState(byte[] state)
    {
        lock (_lock)
        {
            _stateToLoad = state;
            Monitor.PulseAll(_lock);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _stopping = true;
            Monitor.PulseAll(_lock);
        }
    }

    private void Run()
    {
        try
        {
            MameMachine.Start(_set, _chips, _nvram, OnAudio);

            lock (_lock)
            {
                Width = MameMachine.Width;
                Height = MameMachine.Height;
                _front = new Rgba32[Width * Height];
                _back = new Rgba32[Width * Height];
                _frameSeconds = MameMachine.FrameSeconds;
                if (!_stopping)
                    State = MameSessionState.Running;
            }

            while (true)
            {
                FrameJob job;
                lock (_lock)
                {
                    while (!_stopping && _jobs.Count == 0 && _stateToLoad == null && !_saveRequested)
                    {
                        Monitor.Wait(_lock);
                    }

                    if (_stopping)
                        break;

                    if (_stateToLoad is { } state)
                    {
                        _stateToLoad = null;
                        MameMachine.LoadState(state);
                        continue;
                    }

                    if (_saveRequested)
                    {
                        _saveRequested = false;
                        _savedState = MameMachine.SaveState();
                        continue;
                    }

                    job = _jobs.Dequeue();
                }

                MameMachine.RunFrame(job.PlayerOne, job.PlayerTwo);
                MameMachine.CopyFrame(_back);

                lock (_lock)
                {
                    (_front, _back) = (_back, _front);
                    _frameReady = true;
                    if (job.Frame >= 0 && job.Frame % MameNetplay.ChecksumInterval == 0)
                        _checksums.Add((job.Frame, MameMachine.Checksum()));
                }
            }

            lock (_lock)
            {
                State = MameSessionState.Stopped;
            }
        }
        catch (Exception e)
        {
            lock (_lock)
            {
                Error = e.ToString();
                State = MameSessionState.Failed;
            }
        }
        finally
        {
            MameMachine.Stop();
        }
    }

    private void OnAudio(byte[] buffer, int frames)
    {
        var samples = new short[frames * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short) (buffer[i * 2] | (buffer[i * 2 + 1] << 8));
        }

        lock (_lock)
        {
            _audioOut.Add(samples);
        }
    }

    private readonly record struct FrameJob(int Frame, MameControls PlayerOne, MameControls PlayerTwo);
}
