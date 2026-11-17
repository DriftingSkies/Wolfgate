using System.Linq;
using System.Text;
using Content.Server._WG.Announcements;
using Content.Server.GameTicking;
using Content.Shared._WG.Announcements;
using Content.Shared._WG.Defcon;
using Content.Shared.GameTicking;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Defcon;

/// <summary>Scores the sector threat on a timer, moves the DEFCON level and announces each change.</summary>
public sealed partial class WGDefconSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WGAnnounceSystem _announce = default!;

    /// <summary>Most reasons a banner lists.</summary>
    private const int BannerReasons = 3;

    private readonly WGDefconTracker _tracker = new();
    private TimeSpan _next;
    private bool _enabled;
    private string? _missingProfile;

    /// <summary>Callouts waiting for the alarm to finish.</summary>
    private readonly List<(TimeSpan At, string Path, float Volume)> _queued = new();

    /// <summary>The current level, 5 (calmest) to 1.</summary>
    public int Level => _tracker.Level;

    /// <summary>The level an admin pinned, if any.</summary>
    public int? Locked { get; private set; }

    /// <summary>Score an admin added on top of the sources.</summary>
    public float Bias { get; set; }

    /// <summary>The last score and what made it up, strongest first.</summary>
    public float Score { get; private set; }
    public IReadOnlyList<WGDefconContribution> Contributions { get; private set; } = Array.Empty<WGDefconContribution>();
    public int Floor { get; private set; } = WGDefconTracker.Calmest;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        _enabled = _cfg.GetCVar(DefconCVars.Enabled);
        Subs.CVar(_cfg, DefconCVars.Enabled, OnEnabledChanged);
    }

    private void OnEnabledChanged(bool value)
    {
        if (_enabled == value)
            return;

        _enabled = value;
        DirtyPanels();
        RaiseLocalEvent(new WGDefconChangedEvent(_tracker.Level, _tracker.Level));
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        var old = _tracker.Level;
        _tracker.Set(WGDefconTracker.Calmest, TimeSpan.Zero);
        _next = TimeSpan.Zero;
        Locked = null;
        Bias = 0f;
        Score = 0f;
        _queued.Clear();
        Floor = WGDefconTracker.Calmest;
        Contributions = Array.Empty<WGDefconContribution>();
        DirtyPanels();
        if (old != WGDefconTracker.Calmest)
            RaiseLocalEvent(new WGDefconChangedEvent(old, WGDefconTracker.Calmest));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        PlayQueued();
        if (!_enabled || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        var now = _timing.CurTime;
        if (now < _next || Profile() is not { } profile)
            return;

        _next = now + profile.Interval;
        Evaluate(now);
    }

    /// <summary>The profile in force, or null with an error logged once.</summary>
    public WGDefconProfilePrototype? Profile()
    {
        var id = _cfg.GetCVar(DefconCVars.Profile);
        if (_prototypes.TryIndex<WGDefconProfilePrototype>(id, out var profile))
            return profile;

        if (_missingProfile != id)
            Log.Error($"No wgDefconProfile {id}");
        _missingProfile = id;
        return null;
    }

    /// <summary>Open admin panels, refreshed after each score and change.</summary>
    public readonly HashSet<WGDefconEui> Panels = new();

    /// <summary>Scores the threat and steps the level toward it. Returns true when the level changed.</summary>
    public bool Evaluate(TimeSpan now)
    {
        var changed = EvaluateCore(now);
        DirtyPanels();
        return changed;
    }

    private bool EvaluateCore(TimeSpan now)
    {
        if (Profile() is not { } profile)
            return false;

        var ev = new WGDefconGatherEvent(profile, now);
        RaiseLocalEvent(ev);
        if (Bias != 0f)
            ev.Contributions.Add(new WGDefconContribution(Bias, Loc.GetString("wg-defcon-reason-bias")));

        Contributions = ev.Contributions.OrderByDescending(c => c.Score).ToList();
        Score = MathF.Max(0f, ev.Contributions.Sum(c => c.Score));
        Floor = ev.Floor;

        if (Locked != null)
            return false;

        var old = _tracker.Level;
        if (!_tracker.Step(WGDefconTracker.Target(Score, profile.Thresholds, Floor), now, profile.RiseConfirm, profile.FallDelay, profile.MinDwell))
            return false;

        Changed(profile, old, Contributions);
        return true;
    }

    /// <summary>Sets the level now. A locked level holds until <see cref="Unlock"/>.</summary>
    public void SetLevel(int level, bool locked, bool announce = true)
    {
        var old = _tracker.Level;
        _tracker.Set(level, _timing.CurTime);
        Locked = locked ? _tracker.Level : null;
        DirtyPanels();
        if (old == _tracker.Level)
            return;

        if (announce && Profile() is { } profile)
            Changed(profile, old, Array.Empty<WGDefconContribution>());
        else
            RaiseLocalEvent(new WGDefconChangedEvent(old, _tracker.Level));
    }

    /// <summary>Lets the level follow the threat again.</summary>
    public void Unlock()
    {
        Locked = null;
        _tracker.Set(_tracker.Level, _timing.CurTime);
        DirtyPanels();
    }

    /// <summary>What the admin panel shows.</summary>
    public WGDefconEuiState PanelState()
    {
        var state = new WGDefconEuiState
        {
            Enabled = _enabled,
            Level = _tracker.Level,
            Locked = Locked,
            Score = Score,
            Bias = Bias,
            Floor = Floor,
        };

        var profile = Profile();
        for (var level = WGDefconTracker.Calmest; level >= WGDefconTracker.Severest; level--)
        {
            var info = profile != null ? LevelOf(profile, level) : null;
            state.Levels.Add(new WGDefconLevelEntry(level,
                info != null ? Loc.GetString(info.Name) : level.ToString(),
                info?.Color ?? Color.White));
        }

        foreach (var contribution in Contributions)
        {
            state.Reasons.Add(new WGDefconReasonEntry(contribution.Score, contribution.Reason));
        }

        return state;
    }

    private void DirtyPanels()
    {
        foreach (var panel in Panels)
        {
            panel.StateDirty();
        }
    }

    /// <summary>The PDA line for the current level, or null while DEFCON is off.</summary>
    public string? PdaLine()
    {
        if (!_enabled || Profile() is not { } profile)
            return null;

        return Loc.GetString("wg-defcon-pda",
            ("level", _tracker.Level),
            ("color", LevelOf(profile, _tracker.Level)?.Color.ToHex() ?? Color.White.ToHex()));
    }

    /// <summary>A level's name, or its number when the profile has none.</summary>
    public string LevelName(int level)
    {
        return Profile() is { } profile && LevelOf(profile, level) is { } info
            ? Loc.GetString(info.Name)
            : level.ToString();
    }

    private static WGDefconLevel? LevelOf(WGDefconProfilePrototype profile, int level)
    {
        return profile.Levels.GetValueOrDefault(level);
    }

    /// <summary>Plays the alarm (repeated as the level asks) and any siren, then queues the level's callout for when the alarms end.</summary>
    private void PlaySounds(WGDefconProfilePrototype profile, WGDefconLevel? info)
    {
        var delay = TimeSpan.Zero;
        if (Valid(profile.Alarm) is { } alarm)
        {
            var length = _audio.GetAudioLength(_audio.ResolveSound(new SoundPathSpecifier(alarm)));
            Play(alarm, profile.Volume);
            for (var i = 1; i < Math.Max(1, info?.Alarms ?? 1); i++)
            {
                _queued.Add((_timing.CurTime + length * i, alarm, profile.Volume));
            }

            delay = length * Math.Max(1, info?.Alarms ?? 1);
        }

        if (Valid(info?.Siren) is { } siren)
            Play(siren, profile.Volume);

        if (Valid(info?.Callout) is { } callout)
            _queued.Add((_timing.CurTime + delay, callout, profile.Volume));
    }

    private void PlayQueued()
    {
        if (_queued.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _queued.Count - 1; i >= 0; i--)
        {
            if (_queued[i].At > now)
                continue;

            Play(_queued[i].Path, _queued[i].Volume);
            _queued.RemoveAt(i);
        }
    }

    private string? Valid(string? path)
    {
        if (path == null)
            return null;

        if (_announce.IsValidSound(path))
            return path;

        Log.Error($"DEFCON sound {path} is not a sound under /Audio");
        return null;
    }

    private void Play(string path, float volume)
    {
        _audio.PlayGlobal(new SoundPathSpecifier(path), Filter.Broadcast(), true, AudioParams.Default.WithVolume(volume));
    }

    private void Changed(WGDefconProfilePrototype profile, int old, IReadOnlyList<WGDefconContribution> reasons)
    {
        var level = _tracker.Level;
        var rising = level < old;
        var info = LevelOf(profile, level);
        var name = info != null ? Loc.GetString(info.Name) : level.ToString();

        var body = new StringBuilder();
        body.Append(Loc.GetString(rising ? "wg-defcon-body-rising" : "wg-defcon-body-falling", ("level", level), ("name", name)));
        if (info != null)
            body.Append('\n').Append(Loc.GetString(info.Description));

        var shown = reasons.Take(BannerReasons).ToList();
        if (rising && shown.Count > 0)
        {
            body.Append('\n').Append(Loc.GetString("wg-defcon-body-reasons"));
            foreach (var reason in shown)
            {
                body.Append('\n').Append(Loc.GetString("wg-defcon-body-reason", ("reason", reason.Reason)));
            }
        }

        var request = new WGAnnounceRequest
        {
            Sender = Loc.GetString("wg-defcon-sender"),
            Title = Loc.GetString("wg-defcon-title", ("level", level), ("name", name)),
            Body = body.ToString(),
            Accent = info?.Color ?? Color.White,
            PostToChat = true,
            SoundMode = WGAnnounceSoundMode.None,
            ShowChip = true,
            ChipLevel = WGAnnounceChipLevel.Custom,
            ChipTitle = Loc.GetString("wg-defcon-chip-title", ("level", level)),
            ChipText = name,
            ChipDuration = rising ? 20f : 10f,
            ChipFlash = rising && level <= 2,
        };

        if (_announce.Send(request, Filter.Broadcast(), "DEFCON") is { } error)
            Log.Error($"DEFCON {level} announcement refused: {error}");

        PlaySounds(profile, info);

        RaiseLocalEvent(new WGDefconChangedEvent(old, level));
    }
}
