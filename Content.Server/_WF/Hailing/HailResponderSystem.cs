using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Hailing;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Hailing;

/// <summary>
/// Runs the NPCs that answer hails: each answers after its delay, greets the caller, speaks what its grid's hail
/// services add and offers those services as answers to pick.
/// </summary>
public sealed partial class HailResponderSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private HailingSystem _hailing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextUpdate;

    private readonly List<HailOption> _options = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HailOptionChosenEvent>(OnChosen);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;

        var query = EntityQueryEnumerator<HailResponderComponent>();

        while (query.MoveNext(out var uid, out var responder))
        {
            // A call it can no longer hold is ended by the hail itself.
            if (TerminatingOrDeleted(uid) || _mobState.IsIncapacitated(uid))
                continue;

            var manned = responder.Console;
            var found = FindConsole(uid, responder);

            // It has left the console it was on a call at.
            if (manned is { } left && left != found && OnCall(left, uid))
                _hailing.End(left);

            if (found is not { } console)
                continue;

            if (!TryComp<HailConsoleComponent>(console, out var hail))
            {
                responder.Greeted = false;
                continue;
            }

            switch (hail.State)
            {
                case HailState.Ringing:
                    // A new call can ring before a tick sees the last one end.
                    responder.Greeted = false;
                    if (now >= hail.StateTime + TimeSpan.FromSeconds(responder.AnswerDelay))
                        _hailing.AnswerAutomated(console, uid, true);

                    break;

                case HailState.Connected when hail.OperatorUid == uid:
                    if (!responder.Greeted)
                    {
                        responder.Greeted = true;
                        Greet(uid, console, responder, hail);
                    }

                    OfferServices(uid, console, hail);
                    break;
            }
        }
    }

    /// <summary>Greets the caller, then raises each hail service on the grid and speaks what they add.</summary>
    private void Greet(EntityUid uid, EntityUid console, HailResponderComponent responder, HailConsoleComponent hail)
    {
        if (hail.Peer is not { } helm)
            return;

        var grid = Transform(console).GridUid;
        if (responder.Greetings.Count > 0)
        {
            // Each side is named as its radar shows the other, so a ship running dark stays unnamed.
            _hailing.Say(console, uid, Loc.GetString(_random.Pick(responder.Greetings),
                ("name", Identity.Name(uid, EntityManager)),
                ("location", grid is { } station ? _hailing.ShipLabel(helm, station) : Loc.GetString("hail-unknown-vessel")),
                ("ship", hail.PeerGrid is { } ship ? _hailing.ShipLabel(console, ship) : Loc.GetString("hail-unknown-vessel"))));
        }

        var lines = new List<string>();
        var query = EntityQueryEnumerator<HailServiceComponent, TransformComponent>();

        while (query.MoveNext(out var service, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            var ev = new HailServiceGreetEvent(hail.PeerGrid, lines);
            RaiseLocalEvent(service, ref ev);
        }

        foreach (var line in lines)
        {
            _hailing.Say(console, uid, line);
        }
    }

    /// <summary>
    /// Puts the grid's hail services in front of the caller as answers, unless they already have answers or a
    /// service window open.
    /// </summary>
    private void OfferServices(EntityUid uid, EntityUid console, HailConsoleComponent hail)
    {
        if (hail.Peer is not { } helm || !TryComp<HailConsoleComponent>(helm, out var helmHail) ||
            helmHail.Options.Count > 0 || _hailing.HasOpenService(helm))
        {
            return;
        }

        var grid = Transform(console).GridUid;
        _options.Clear();
        var query = EntityQueryEnumerator<HailServiceComponent, TransformComponent>();

        while (query.MoveNext(out var service, out var comp, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            _options.Add(new HailOption(OptionId(service), Loc.GetString(comp.Name)));

            if (_options.Count == HailConsoleComponent.MaxOptions)
                break;
        }

        if (_options.Count > 0)
            _hailing.Offer(console, uid, _options);
    }

    /// <summary>Opens the service the caller picked on their helm.</summary>
    private void OnChosen(ref HailOptionChosenEvent args)
    {
        if (args.OffererGrid is not { } grid || !TryComp<HailConsoleComponent>(args.Offerer, out var hail) ||
            hail.OperatorUid is not { } responder || !HasComp<HailResponderComponent>(responder))
        {
            return;
        }

        var query = EntityQueryEnumerator<HailServiceComponent, TransformComponent>();

        while (query.MoveNext(out var service, out _, out var xform))
        {
            if (xform.GridUid != grid || OptionId(service) != args.Id)
                continue;

            _hailing.OpenService(args.Chooser, service, args.ChooserEntity);
            return;
        }
    }

    private string OptionId(EntityUid service)
    {
        return $"hail-service-{GetNetEntity(service)}";
    }

    /// <summary>Whether the responder holds the call at a console.</summary>
    private bool OnCall(EntityUid console, EntityUid uid)
    {
        return TryComp<HailConsoleComponent>(console, out var hail) && hail.Automated && hail.OperatorUid == uid;
    }

    /// <summary>Keeps the console it mans if still valid, otherwise takes the nearest powered one in range.</summary>
    private EntityUid? FindConsole(EntityUid uid, HailResponderComponent responder)
    {
        if (responder.Console is { } cached && IsManned(uid, cached, responder.ConsoleRange))
            return cached;

        responder.Console = null;

        if (Transform(uid).GridUid is not { } grid)
            return null;

        var origin = _transform.GetWorldPosition(uid);
        var nearest = responder.ConsoleRange;
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();

        while (query.MoveNext(out var console, out _, out var xform))
        {
            if (xform.GridUid != grid || !this.IsPowered(console, EntityManager))
                continue;

            var distance = (_transform.GetWorldPosition(console) - origin).Length();

            if (distance > nearest)
                continue;

            nearest = distance;
            responder.Console = console;
        }

        return responder.Console;
    }

    /// <summary>Whether the console is still powered, on the responder's grid, and in range.</summary>
    private bool IsManned(EntityUid uid, EntityUid console, float range)
    {
        if (TerminatingOrDeleted(console) || !HasComp<ShuttleConsoleComponent>(console) ||
            Transform(console).GridUid != Transform(uid).GridUid || !this.IsPowered(console, EntityManager))
        {
            return false;
        }

        return (_transform.GetWorldPosition(console) - _transform.GetWorldPosition(uid)).Length() <= range;
    }
}
