using Content.Shared._WG.Pool;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Pool;

/// <summary>
/// Runs the 8-ball game of every pool table: opens the window, seats the players and plays the shots they send.
/// The server works out each shot to its end and clients replay it.
/// </summary>
public sealed partial class PoolSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private WGPoolTableSystem _tables = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGPoolTableComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<WGPoolTableComponent, InteractUsingEvent>(OnInteractUsing);

        Subs.BuiEvents<WGPoolTableComponent>(WGPoolUiKey.Key, subs =>
        {
            subs.Event<BoundUIClosedEvent>(OnClosed);
            subs.Event<PoolSeatMessage>(OnSeat);
            subs.Event<PoolPlaceCueMessage>(OnPlaceCue);
            subs.Event<PoolShootMessage>(OnShoot);
            subs.Event<PoolRerackMessage>(OnRerack);
        });
    }

    private void OnActivate(Entity<WGPoolTableComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        Open(ent, args.User);
        args.Handled = true;
    }

    private void OnInteractUsing(Entity<WGPoolTableComponent> ent, ref InteractUsingEvent args)
    {
        // A cue opens the game; anything else is put down on the table as usual.
        if (args.Handled || !HasComp<WGPoolCueComponent>(args.Used))
            return;

        Open(ent, args.User);
        args.Handled = true;
    }

    private void Open(Entity<WGPoolTableComponent> piece, EntityUid user)
    {
        var root = _tables.GetRoot(piece);
        if (!TryComp(root, out WGPoolTableComponent? table))
            return;

        table.Game ??= new PoolGame(new System.Random(_random.Next()));
        UpdateUi((root, table));
        _ui.OpenUi(root, WGPoolUiKey.Key, user);
    }

    private void OnClosed(Entity<WGPoolTableComponent> ent, ref BoundUIClosedEvent args)
    {
        if (SeatOf(ent, args.Actor) is not { } seat)
            return;

        ent.Comp.Seats[seat] = null;
        UpdateUi(ent);
    }

    private void OnSeat(Entity<WGPoolTableComponent> ent, ref PoolSeatMessage args)
    {
        var current = SeatOf(ent, args.Actor);
        if (args.Seat < 0)
        {
            if (current is not { } seat)
                return;

            ent.Comp.Seats[seat] = null;
        }
        else
        {
            if (current != null || args.Seat >= ent.Comp.Seats.Length || ent.Comp.Seats[args.Seat] != null)
                return;

            ent.Comp.Seats[args.Seat] = args.Actor;
        }

        UpdateUi(ent);
    }

    private void OnPlaceCue(Entity<WGPoolTableComponent> ent, ref PoolPlaceCueMessage args)
    {
        if (CanPlay(ent, args.Actor))
            ent.Comp.Game?.TryPlaceCue(args.Position);

        // Sent either way, so a refused placement snaps back on the client.
        UpdateUi(ent);
    }

    private void OnShoot(Entity<WGPoolTableComponent> ent, ref PoolShootMessage args)
    {
        if (ent.Comp.Game is not { } game || !CanPlay(ent, args.Actor) || !game.TryShoot(args.Angle, args.Power, out var seconds))
        {
            UpdateUi(ent);
            return;
        }

        ent.Comp.BusyUntil = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        UpdateUi(ent);
    }

    private void OnRerack(Entity<WGPoolTableComponent> ent, ref PoolRerackMessage args)
    {
        if (SeatOf(ent, args.Actor) == null || _timing.CurTime < ent.Comp.BusyUntil)
            return;

        // The winner breaks the next game.
        var breaker = ent.Comp.Game is { Winner: >= 0 } last ? last.Winner : 0;
        ent.Comp.Game = new PoolGame(new System.Random(_random.Next()), breaker);
        UpdateUi(ent);
    }

    private static int? SeatOf(Entity<WGPoolTableComponent> ent, EntityUid user)
    {
        for (var seat = 0; seat < ent.Comp.Seats.Length; seat++)
        {
            if (ent.Comp.Seats[seat] == user)
                return seat;
        }

        return null;
    }

    /// <summary>
    /// Whether this player may touch the cue ball now: seated, cue in hand, the balls at rest, and either their
    /// turn or nobody on the other side, so one player can practise alone.
    /// </summary>
    private bool CanPlay(Entity<WGPoolTableComponent> ent, EntityUid user)
    {
        if (ent.Comp.Game is not { Winner: < 0 } game || _timing.CurTime < ent.Comp.BusyUntil)
            return false;

        if (SeatOf(ent, user) is not { } seat || !_tables.HoldsCue(user))
            return false;

        return game.Turn == seat || ent.Comp.Seats[game.Turn] == null;
    }

    private void UpdateUi(Entity<WGPoolTableComponent> ent)
    {
        if (ent.Comp.Game is not { } game)
            return;

        var seats = new NetEntity?[ent.Comp.Seats.Length];
        var names = new string?[ent.Comp.Seats.Length];
        for (var seat = 0; seat < seats.Length; seat++)
        {
            if (ent.Comp.Seats[seat] is not { } player || TerminatingOrDeleted(player))
            {
                ent.Comp.Seats[seat] = null;
                continue;
            }

            seats[seat] = GetNetEntity(player);
            names[seat] = Identity.Name(player, EntityManager);
        }

        _ui.SetUiState(ent.Owner, WGPoolUiKey.Key, new PoolUiState
        {
            Balls = (System.Numerics.Vector2[]) game.Pos.Clone(),
            Down = game.Down,
            Turn = game.Turn,
            Groups = (PoolGroup[]) game.Groups.Clone(),
            BallInHand = game.BallInHand,
            Breaking = game.Breaking,
            Winner = game.Winner,
            Outcome = game.Outcome,
            Seats = seats,
            SeatNames = names,
            ShotId = game.ShotId,
            ShotStart = (System.Numerics.Vector2[]) game.ShotStart.Clone(),
            ShotStartDown = game.ShotStartDown,
            ShotVelocity = game.ShotVelocity,
        });
    }
}
