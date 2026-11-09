using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared._WF.Lattice;
using Content.Shared.Chat;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// What each band does that the others don't. In Dissonance time runs wrong: a door someone opens waits a beat first.
/// In Feedback the radio and the air give a person's own words back to them, a little wrong. (Detune's dulled sound is
/// on the client, in <c>WFDriftSoundSystem</c>; the Coda has everything else.)
/// </summary>
public sealed class WFBandSignatureSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedDoorSystem _doors = default!;

    private const int DissonanceBand = 2;
    private const int FeedbackBand = 3;

    /// <summary>How long a door waits in Dissonance before it opens.</summary>
    private static readonly TimeSpan DoorLag = TimeSpan.FromSeconds(0.7);

    /// <summary>Chance a person's words come back to them in Feedback.</summary>
    private const float EchoChance = 0.3f;

    private readonly List<(EntityUid Door, EntityUid? User, TimeSpan When)> _waiting = new();
    private readonly HashSet<EntityUid> _letThrough = new();
    private readonly List<(ICommonSession Session, string Message, TimeSpan When)> _echoes = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoorComponent, BeforeDoorOpenedEvent>(OnBeforeOpen);
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
    }

    private void OnBeforeOpen(Entity<DoorComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (args.Cancelled || args.User is not { } user || !HasComp<ActorComponent>(user) || Band(ent.Owner) != DissonanceBand)
            return;

        if (_letThrough.Remove(ent.Owner))
            return;

        args.Cancel();
        if (!_waiting.Exists(w => w.Door == ent.Owner))
            _waiting.Add((ent.Owner, user, _timing.CurTime + DoorLag));
    }

    private void OnSpoke(EntitySpokeEvent args)
    {
        if (!TryComp<ActorComponent>(args.Source, out var actor)
            || HasComp<WFChoirMemberComponent>(args.Source)
            || Band(args.Source) != FeedbackBand
            || !_random.Prob(EchoChance))
            return;

        var back = WFLatticeMath.Garble(args.Message, 0.15f, _random.Next());
        _echoes.Add((actor.PlayerSession, back, _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(1.5f, 4f))));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        for (var i = _waiting.Count - 1; i >= 0; i--)
        {
            var (door, user, when) = _waiting[i];
            if (now < when)
                continue;

            _waiting.RemoveAt(i);
            if (TerminatingOrDeleted(door))
                continue;

            _letThrough.Add(door);
            _doors.TryOpen(door, user: user);
            _letThrough.Remove(door);
        }

        for (var i = _echoes.Count - 1; i >= 0; i--)
        {
            var (session, message, when) = _echoes[i];
            if (now < when)
                continue;

            _echoes.RemoveAt(i);
            var name = session.AttachedEntity is { } mob ? Name(mob) : session.Name;
            var wrapped = Loc.GetString("wf-lattice-feedback-wrap", ("name", name), ("message", message));
            _chat.ChatMessageToOne(ChatChannel.Radio, message, wrapped, EntityUid.Invalid, false, session.Channel);
        }
    }

    /// <summary>The band something is in: its grid's, or its own off one.</summary>
    private int Band(EntityUid uid)
    {
        if (Transform(uid).GridUid is { } grid && TryComp<WFDriftComponent>(grid, out var ship))
            return ship.Band;

        return CompOrNull<WFDriftComponent>(uid)?.Band ?? 0;
    }
}
