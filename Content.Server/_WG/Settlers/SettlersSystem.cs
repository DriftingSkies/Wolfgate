using Content.Shared._WG.Settlers;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Robust.Shared.Random;

namespace Content.Server._WG.Settlers;

/// <summary>
/// Runs the Settlers of Colossus game on every board: opens the window, seats the players and applies their moves.
/// Each viewer is sent the board with only their own hand in it.
/// </summary>
public sealed class SettlersSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGSettlersBoardComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<WGSettlersBoardComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WGSettlersBoardComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WGSettlersBoardComponent, ExaminedEvent>(OnExamined);

        Subs.BuiEvents<WGSettlersBoardComponent>(WGSettlersUiKey.Key, subs =>
        {
            subs.Event<BoundUIClosedEvent>(OnClosed);
            subs.Event<SettlersRequestStateMessage>(OnRequestState);
            subs.Event<SettlersSeatMessage>(OnSeat);
            subs.Event<SettlersStartMessage>(OnStart);
            subs.Event<SettlersNewGameMessage>(OnNewGame);
            subs.Event<SettlersActionMessage>(OnAction);
        });
    }

    private void OnActivate(Entity<WGSettlersBoardComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        Open(ent, args.User);
        args.Handled = true;
    }

    private void OnUseInHand(Entity<WGSettlersBoardComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        Open(ent, args.User);
        args.Handled = true;
    }

    private void OnGetVerbs(Entity<WGSettlersBoardComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("wg-settlers-verb-play"),
            Act = () => Open(ent, user),
        });
    }

    /// <summary>Tells an examiner when a game is being played on the board.</summary>
    private void OnExamined(Entity<WGSettlersBoardComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Game is { State.Running: true })
            args.PushMarkup(Loc.GetString("wg-settlers-examine-running"));
    }

    /// <summary>Opens the board's window. The window asks for the board once it is up.</summary>
    public void Open(Entity<WGSettlersBoardComponent> ent, EntityUid user)
    {
        ent.Comp.Game ??= new SettlersGame(new System.Random(_random.Next()));
        _ui.OpenUi(ent.Owner, WGSettlersUiKey.Key, user);
    }

    private void OnClosed(Entity<WGSettlersBoardComponent> ent, ref BoundUIClosedEvent args)
    {
        if (SeatOf(ent, args.Actor) is not { } seat)
            return;

        Vacate(ent, seat);
        UpdateUi(ent);
    }

    private void OnRequestState(Entity<WGSettlersBoardComponent> ent, ref SettlersRequestStateMessage args)
    {
        SendTo(ent, args.Actor);
    }

    /// <summary>Before the game a player may take any free seat; once it runs, only a seat in the game left vacant.</summary>
    private void OnSeat(Entity<WGSettlersBoardComponent> ent, ref SettlersSeatMessage args)
    {
        if (ent.Comp.Game is not { } game)
            return;

        var current = SeatOf(ent, args.Actor);
        if (args.Seat < 0)
        {
            if (current is not { } seat)
                return;

            Vacate(ent, seat);
        }
        else
        {
            if (current != null || args.Seat >= ent.Comp.Seats.Length || ent.Comp.Seats[args.Seat] != null)
                return;

            if (game.State.Running && !game.State.Seated(args.Seat))
                return;

            ent.Comp.Seats[args.Seat] = args.Actor;
            game.SetPresent(args.Seat, true);
        }

        UpdateUi(ent);
    }

    private void OnStart(Entity<WGSettlersBoardComponent> ent, ref SettlersStartMessage args)
    {
        if (ent.Comp.Game is not { } game || SeatOf(ent, args.Actor) == null || !game.Start())
            return;

        UpdateUi(ent);
    }

    /// <summary>
    /// Clears the board. Mid-game only a player left alone at it may, so a game isn't thrown away under the
    /// others; whoever is seated stays seated for the next.
    /// </summary>
    private void OnNewGame(Entity<WGSettlersBoardComponent> ent, ref SettlersNewGameMessage args)
    {
        if (ent.Comp.Game is not { } game || SeatOf(ent, args.Actor) == null)
            return;

        if (game.State.Running && SeatedCount(ent) > 1)
            return;

        var fresh = new SettlersGame(new System.Random(_random.Next()));
        for (var seat = 0; seat < ent.Comp.Seats.Length; seat++)
        {
            if (ent.Comp.Seats[seat] != null)
                fresh.SetPresent(seat, true);
        }

        ent.Comp.Game = fresh;
        UpdateUi(ent);
    }

    private void OnAction(Entity<WGSettlersBoardComponent> ent, ref SettlersActionMessage args)
    {
        if (ent.Comp.Game is not { } game || SeatOf(ent, args.Actor) is not { } seat || !game.TryAct(seat, args.Action))
        {
            // A refused move puts the sender's window back as it was.
            SendTo(ent, args.Actor);
            return;
        }

        UpdateUi(ent);
    }

    private void Vacate(Entity<WGSettlersBoardComponent> ent, int seat)
    {
        ent.Comp.Seats[seat] = null;
        ent.Comp.Game?.SetPresent(seat, false);
    }

    private static int? SeatOf(Entity<WGSettlersBoardComponent> ent, EntityUid user)
    {
        for (var seat = 0; seat < ent.Comp.Seats.Length; seat++)
        {
            if (ent.Comp.Seats[seat] == user)
                return seat;
        }

        return null;
    }

    private static int SeatedCount(Entity<WGSettlersBoardComponent> ent)
    {
        var count = 0;
        foreach (var seat in ent.Comp.Seats)
        {
            if (seat != null)
                count++;
        }

        return count;
    }

    /// <summary>Sends every viewer the board as they may see it.</summary>
    private void UpdateUi(Entity<WGSettlersBoardComponent> ent)
    {
        for (var seat = 0; seat < ent.Comp.Seats.Length; seat++)
        {
            if (ent.Comp.Seats[seat] is { } player && TerminatingOrDeleted(player))
                Vacate(ent, seat);
        }

        foreach (var actor in _ui.GetActors(ent.Owner, WGSettlersUiKey.Key))
        {
            SendTo(ent, actor);
        }
    }

    private void SendTo(Entity<WGSettlersBoardComponent> ent, EntityUid actor)
    {
        if (ent.Comp.Game is not { } game)
            return;

        var seats = new NetEntity?[ent.Comp.Seats.Length];
        var names = new string?[ent.Comp.Seats.Length];
        for (var seat = 0; seat < seats.Length; seat++)
        {
            if (ent.Comp.Seats[seat] is not { } player || TerminatingOrDeleted(player))
                continue;

            seats[seat] = GetNetEntity(player);
            names[seat] = Identity.Name(player, EntityManager);
        }

        var state = game.State.CopyFor(SeatOf(ent, actor));
        _ui.ServerSendUiMessage(ent.Owner, WGSettlersUiKey.Key, new SettlersStateMessage(state, seats, names), actor);
    }
}
