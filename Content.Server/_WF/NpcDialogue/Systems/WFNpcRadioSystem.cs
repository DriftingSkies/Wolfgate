using System.Numerics;
using Content.Server.Radio;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Radio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcDialogue.Systems;

/// <summary>How much an NPC radio line matters when the air is busy.</summary>
public enum WFNpcRadioPriority : byte
{
    /// <summary>Comings and goings: the first thing dropped.</summary>
    Routine,

    /// <summary>Warnings, threats and taunts.</summary>
    Normal,

    /// <summary>Maydays and surrenders: always sent.</summary>
    High,
}

/// <summary>
/// Puts NPC lines on the radio within a budget. A line meant for one vessel is heard only near the speaker and that
/// vessel; routine traffic is capped across the sector; two crews talking over each other on a channel keep the first.
/// </summary>
public sealed partial class WFNpcRadioSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How far from the speaker or its target a targeted line carries.</summary>
    public const float ScopeRange = 3000f;

    /// <summary>Another crew's line this soon after one on the same channel is talking over it.</summary>
    private static readonly TimeSpan Overlap = TimeSpan.FromSeconds(3);

    /// <summary>Routine lines one channel carries a minute, sector-wide.</summary>
    private const int RoutinePerMinute = 8;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, Queue<TimeSpan>> _routine = new();
    private readonly Dictionary<(string Channel, EntityUid? Near), (TimeSpan At, EntityUid Speaker)> _last = new();

    private MapId _scopeMap = MapId.Nullspace;
    private readonly List<Vector2> _scope = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnReceiveAttempt);
    }

    /// <summary>
    /// Sends a line as <paramref name="speaker"/>. With <paramref name="near"/> it carries only around the speaker's
    /// ship and that vessel. False when the budget dropped it.
    /// </summary>
    public bool Send(EntityUid speaker, string text, ProtoId<RadioChannelPrototype> channel,
        WFNpcRadioPriority priority = WFNpcRadioPriority.Normal, EntityUid? near = null)
    {
        var now = _timing.CurTime;
        var shipOf = Transform(speaker).GridUid ?? speaker;
        if (priority != WFNpcRadioPriority.High)
        {
            if (_last.TryGetValue((channel.Id, near), out var last) && now - last.At < Overlap
                && !TerminatingOrDeleted(last.Speaker) && (Transform(last.Speaker).GridUid ?? last.Speaker) != shipOf)
                return false;

            if (priority == WFNpcRadioPriority.Routine && near == null)
            {
                if (!_routine.TryGetValue(channel.Id, out var sent))
                    _routine[channel.Id] = sent = new Queue<TimeSpan>();
                while (sent.Count > 0 && now - sent.Peek() > Window)
                {
                    sent.Dequeue();
                }

                if (sent.Count >= RoutinePerMinute)
                    return false;
                sent.Enqueue(now);
            }
        }

        if (_last.Count > 256)
        {
            foreach (var (key, entry) in _last)
            {
                if (now - entry.At >= Overlap)
                    _last.Remove(key);
            }
        }

        _last[(channel.Id, near)] = (now, speaker);
        if (near is { } target && !TerminatingOrDeleted(target))
        {
            var here = _transform.GetMapCoordinates(speaker);
            var there = _transform.GetMapCoordinates(target);
            _scopeMap = here.MapId;
            _scope.Clear();
            _scope.Add(here.Position);
            if (there.MapId == here.MapId)
                _scope.Add(there.Position);
        }

        try
        {
            _radio.SendRadioMessage(speaker, text, channel, speaker);
        }
        finally
        {
            _scopeMap = MapId.Nullspace;
            _scope.Clear();
        }

        return true;
    }

    /// <summary>Forgets what has been sent, so the budget starts empty.</summary>
    public void Reset()
    {
        _routine.Clear();
        _last.Clear();
    }

    /// <summary>While a targeted line is sent, radios far from both ends do not hear it.</summary>
    private void OnReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        if (_scope.Count == 0 || args.Cancelled)
            return;

        var at = _transform.GetMapCoordinates(args.RadioReceiver);
        if (at.MapId != _scopeMap)
        {
            args.Cancelled = true;
            return;
        }

        foreach (var centre in _scope)
        {
            if ((at.Position - centre).LengthSquared() <= ScopeRange * ScopeRange)
                return;
        }

        args.Cancelled = true;
    }
}
