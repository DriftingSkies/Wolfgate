namespace Content.Shared._WG.Settlers;

/// <summary>
/// One game of Settlers of Colossus: lays out a random board, checks and applies every move, and plays out the
/// turns and choices of players who have left their seats. The contract deck lives only here, never in the state.
/// </summary>
public sealed class SettlersGame
{
    private readonly System.Random _random;
    private readonly List<SettlersCard> _deck = new();

    public readonly SettlersState State = new();

    public SettlersGame(System.Random random)
    {
        _random = random;
        LayBoard();
        Array.Fill(State.Bank, SettlersRules.BankStock);

        for (var card = 0; card < SettlersRules.CardCounts.Length; card++)
        {
            for (var i = 0; i < SettlersRules.CardCounts[card]; i++)
            {
                _deck.Add((SettlersCard) card);
            }
        }

        Shuffle(_deck);
        State.DeckCount = _deck.Count;
    }

    private void LayBoard()
    {
        var sectors = new List<SettlersSector>();
        for (var sector = 0; sector < SettlersRules.SectorCounts.Length; sector++)
        {
            for (var i = 0; i < SettlersRules.SectorCounts[sector]; i++)
            {
                sectors.Add((SettlersSector) sector);
            }
        }

        Shuffle(sectors);
        var producing = new List<int>();
        for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
        {
            State.Sectors[tile] = sectors[tile];
            if (sectors[tile] == SettlersSector.DeadZone)
                State.Pirates = tile;
            else
                producing.Add(tile);
        }

        // Shuffled until no 6 or 8 sits beside another.
        var numbers = new List<int>(SettlersRules.Numbers);
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            Shuffle(numbers);
            Array.Clear(State.Numbers);
            for (var i = 0; i < producing.Count; i++)
            {
                State.Numbers[producing[i]] = numbers[i];
            }

            if (HotNumbersApart())
                break;
        }

        var docks = new List<int>(SettlersRules.Docks);
        Shuffle(docks);
        for (var i = 0; i < docks.Count; i++)
        {
            State.Docks[i] = docks[i];
        }
    }

    private bool HotNumbersApart()
    {
        for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
        {
            if (!SettlersRules.IsHot(State.Numbers[tile]))
                continue;

            foreach (var neighbour in SettlersBoard.TileNeighbours[tile])
            {
                if (SettlersRules.IsHot(State.Numbers[neighbour]))
                    return false;
            }
        }

        return true;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Marks a seat as sat in or left. Leaving withdraws the seat's offer and lets the game play on without it.</summary>
    public void SetPresent(int seat, bool present)
    {
        State.Players[seat].Present = present;
        if (!present)
            State.Offers[seat] = null;

        Autoplay();
    }

    /// <summary>Starts the game with everyone seated, in a random order.</summary>
    public bool Start()
    {
        if (State.Phase != SettlersPhase.Lobby)
            return false;

        var seats = new List<int>();
        for (var seat = 0; seat < State.Players.Length; seat++)
        {
            if (State.Players[seat].Present)
                seats.Add(seat);
        }

        if (seats.Count < SettlersRules.MinPlayers)
            return false;

        var first = _random.Next(seats.Count);
        var order = new int[seats.Count * 2];
        for (var i = 0; i < seats.Count; i++)
        {
            var seat = seats[(first + i) % seats.Count];
            State.Players[seat].InGame = true;
            order[i] = seat;
            order[order.Length - 1 - i] = seat;
        }

        State.SetupOrder = order;
        State.SetupStep = 0;
        State.Turn = order[0];
        State.Phase = SettlersPhase.SetupOutpost;
        Log(SettlersLogKind.Started, State.Turn);
        Autoplay();
        return true;
    }

    /// <summary>Applies a seated player's move if the rules allow it.</summary>
    public bool TryAct(int seat, SettlersAction action)
    {
        if (!State.Running || !State.Seated(seat) || !State.Players[seat].Present)
            return false;

        var turn = seat == State.Turn;
        var done = action.Kind switch
        {
            SettlersActionKind.Roll => turn && RollDice(_random.Next(1, 7), _random.Next(1, 7)),
            SettlersActionKind.BuildLane => turn && BuildLane(action.Target),
            SettlersActionKind.BuildOutpost => turn && BuildOutpost(action.Target),
            SettlersActionKind.BuildColony => turn && BuildColony(action.Target),
            SettlersActionKind.BuyCard => turn && BuyCard(),
            SettlersActionKind.PlayCard => turn && PlayCard(action.Target, action.First, action.Second),
            SettlersActionKind.EndSurvey => turn && EndSurvey(),
            SettlersActionKind.MovePirates => turn && MovePirates(action.Target),
            SettlersActionKind.Steal => turn && Steal(action.Target),
            SettlersActionKind.Discard => Discard(seat, action.Give),
            SettlersActionKind.BankTrade => turn && BankTrade(action.First, action.Second),
            SettlersActionKind.Offer => Offer(seat, action.Give, action.Want),
            SettlersActionKind.Accept => Accept(seat, action.Target, action.Give, action.Want),
            SettlersActionKind.EndTurn => turn && State.Phase == SettlersPhase.Main && EndTurn(),
            _ => false,
        };

        if (done)
            Autoplay();

        return done;
    }

    /// <summary>Rolls the given dice for the player whose turn it is, so tests can pick them.</summary>
    public bool Roll(int die1, int die2)
    {
        if (!RollDice(die1, die2))
            return false;

        Autoplay();
        return true;
    }

    private bool RollDice(int die1, int die2)
    {
        if (State.Phase != SettlersPhase.Roll)
            return false;

        State.Die1 = die1;
        State.Die2 = die2;
        State.Rolled = true;
        State.RollCount++;
        var sum = die1 + die2;
        Log(SettlersLogKind.Rolled, State.Turn, value: sum);

        if (sum != 7)
        {
            Produce(sum);
            State.Phase = SettlersPhase.Main;
            return true;
        }

        var owing = false;
        for (var seat = 0; seat < State.Players.Length; seat++)
        {
            var size = SettlersRules.Total(State.Players[seat].Hand);
            State.Discard[seat] = State.Players[seat].InGame && size > SettlersRules.HandLimit ? size / 2 : 0;
            owing |= State.Discard[seat] > 0;
        }

        State.Phase = owing ? SettlersPhase.Discard : SettlersPhase.MovePirates;
        return true;
    }

    /// <summary>
    /// Pays out every tile showing the number. When the bank can't cover a resource, nobody gets it, unless only
    /// one player is owed it, who then gets what is left.
    /// </summary>
    private void Produce(int number)
    {
        var owed = new int[State.Players.Length * SettlersRules.ResourceCount];
        for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
        {
            if (State.Numbers[tile] != number || tile == State.Pirates || SettlersRules.Yield(State.Sectors[tile]) is not { } yield)
                continue;

            foreach (var vertex in SettlersBoard.TileVertices[tile])
            {
                var owner = State.VertexOwner[vertex];
                if (owner >= 0)
                    owed[owner * SettlersRules.ResourceCount + (int) yield] += State.Colony[vertex] ? 2 : 1;
            }
        }

        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            var total = 0;
            var owedTo = 0;
            var last = -1;
            for (var seat = 0; seat < State.Players.Length; seat++)
            {
                var amount = owed[seat * SettlersRules.ResourceCount + resource];
                if (amount == 0)
                    continue;

                total += amount;
                owedTo++;
                last = seat;
            }

            if (total == 0)
                continue;

            if (total <= State.Bank[resource])
            {
                for (var seat = 0; seat < State.Players.Length; seat++)
                {
                    Give(seat, resource, owed[seat * SettlersRules.ResourceCount + resource]);
                }
            }
            else if (owedTo == 1)
            {
                Give(last, resource, State.Bank[resource]);
            }
        }
    }

    private void Give(int seat, int resource, int amount)
    {
        State.Bank[resource] -= amount;
        State.Players[seat].Hand[resource] += amount;
    }

    private void Pay(int seat, int[] cost)
    {
        for (var resource = 0; resource < cost.Length; resource++)
        {
            Give(seat, resource, -cost[resource]);
        }
    }

    private bool BuildOutpost(int vertex)
    {
        var seat = State.Turn;
        if (State.Phase == SettlersPhase.SetupOutpost)
        {
            if (!State.CanPlaceOutpost(seat, vertex))
                return false;

            State.VertexOwner[vertex] = seat;
            State.SetupOutpost = vertex;
            State.Phase = SettlersPhase.SetupLane;

            // The second outpost of setup starts its owner off with one of everything around it.
            if (State.SetupStep >= State.SetupOrder.Length / 2)
            {
                foreach (var tile in SettlersBoard.VertexTiles[vertex])
                {
                    if (SettlersRules.Yield(State.Sectors[tile]) is { } yield && State.Bank[(int) yield] > 0)
                        Give(seat, (int) yield, 1);
                }
            }

            UpdateRoute();
            return true;
        }

        if (State.Phase != SettlersPhase.Main || !State.CanPlaceOutpost(seat, vertex) || !State.CanAfford(seat, SettlersRules.OutpostCost))
            return false;

        Pay(seat, SettlersRules.OutpostCost);
        State.VertexOwner[vertex] = seat;
        Log(SettlersLogKind.Built, seat, value: 1);

        // An outpost can cut a rival's route in two.
        UpdateRoute();
        CheckWin();
        return true;
    }

    private bool BuildColony(int vertex)
    {
        var seat = State.Turn;
        if (State.Phase != SettlersPhase.Main || !State.CanPlaceColony(seat, vertex) || !State.CanAfford(seat, SettlersRules.ColonyCost))
            return false;

        Pay(seat, SettlersRules.ColonyCost);
        State.Colony[vertex] = true;
        Log(SettlersLogKind.Upgraded, seat);
        CheckWin();
        return true;
    }

    private bool BuildLane(int edge)
    {
        var seat = State.Turn;
        switch (State.Phase)
        {
            case SettlersPhase.SetupLane:
                if (!State.CanPlaceLane(seat, edge))
                    return false;

                State.EdgeOwner[edge] = seat;
                UpdateRoute();
                AdvanceSetup();
                return true;

            case SettlersPhase.Survey:
                if (!State.CanPlaceLane(seat, edge))
                    return false;

                State.EdgeOwner[edge] = seat;
                State.FreeLanes--;
                UpdateRoute();
                if (State.FreeLanes <= 0 || !State.AnyLane(seat))
                    EndSurvey();

                CheckWin();
                return true;

            case SettlersPhase.Main:
                if (!State.CanPlaceLane(seat, edge) || !State.CanAfford(seat, SettlersRules.LaneCost))
                    return false;

                Pay(seat, SettlersRules.LaneCost);
                State.EdgeOwner[edge] = seat;
                Log(SettlersLogKind.Built, seat, value: 0);
                UpdateRoute();
                CheckWin();
                return true;

            default:
                return false;
        }
    }

    private void AdvanceSetup()
    {
        State.SetupStep++;
        State.SetupOutpost = -1;
        if (State.SetupStep >= State.SetupOrder.Length)
        {
            BeginTurn(State.SetupOrder[0]);
            return;
        }

        State.Turn = State.SetupOrder[State.SetupStep];
        State.Phase = SettlersPhase.SetupOutpost;
    }

    private bool BuyCard()
    {
        var seat = State.Turn;
        if (State.Phase != SettlersPhase.Main || _deck.Count == 0 || !State.CanAfford(seat, SettlersRules.CardCost))
            return false;

        Pay(seat, SettlersRules.CardCost);
        var card = _deck[^1];
        _deck.RemoveAt(_deck.Count - 1);
        State.DeckCount = _deck.Count;
        State.Players[seat].NewCards.Add(card);
        Log(SettlersLogKind.BoughtCard, seat);
        CheckWin();
        return true;
    }

    /// <summary>Plays a contract held since before this turn: one a turn, before or after the roll.</summary>
    private bool PlayCard(int target, int first, int second)
    {
        var seat = State.Turn;
        var player = State.Players[seat];
        if (State.Phase is not (SettlersPhase.Roll or SettlersPhase.Main) || State.PlayedCard || target < 0 || target >= SettlersRules.CardCounts.Length)
            return false;

        var card = (SettlersCard) target;
        if (card == SettlersCard.Charter || !player.Cards.Contains(card))
            return false;

        switch (card)
        {
            case SettlersCard.Escort:
                player.Escorts++;
                if (player.Escorts >= SettlersRules.MinFleet
                    && (State.FleetHolder < 0 || player.Escorts > State.Players[State.FleetHolder].Escorts))
                {
                    State.FleetHolder = seat;
                }

                State.Phase = SettlersPhase.MovePirates;
                break;

            case SettlersCard.Survey:
                if (!State.AnyLane(seat))
                    return false;

                State.FreeLanes = SettlersRules.SurveyLanes;
                State.Phase = SettlersPhase.Survey;
                break;

            case SettlersCard.Windfall:
                if (!IsResource(first) || !IsResource(second))
                    return false;

                if (first == second ? State.Bank[first] < 2 : State.Bank[first] < 1 || State.Bank[second] < 1)
                    return false;

                Give(seat, first, 1);
                Give(seat, second, 1);
                break;

            case SettlersCard.Embargo:
                if (!IsResource(first))
                    return false;

                for (var other = 0; other < State.Players.Length; other++)
                {
                    if (other == seat)
                        continue;

                    player.Hand[first] += State.Players[other].Hand[first];
                    State.Players[other].Hand[first] = 0;
                }

                break;
        }

        player.Cards.Remove(card);
        State.PlayedCard = true;
        Log(SettlersLogKind.PlayedCard, seat, value: (int) card);
        CheckWin();
        return true;
    }

    private bool EndSurvey()
    {
        if (State.Phase != SettlersPhase.Survey)
            return false;

        State.FreeLanes = 0;
        State.Phase = State.Rolled ? SettlersPhase.Main : SettlersPhase.Roll;
        return true;
    }

    private bool Discard(int seat, int[] give)
    {
        if (State.Phase != SettlersPhase.Discard || State.Discard[seat] <= 0 || give.Length != SettlersRules.ResourceCount)
            return false;

        for (var resource = 0; resource < give.Length; resource++)
        {
            if (give[resource] < 0 || give[resource] > State.Players[seat].Hand[resource])
                return false;
        }

        if (SettlersRules.Total(give) != State.Discard[seat])
            return false;

        for (var resource = 0; resource < give.Length; resource++)
        {
            Give(seat, resource, -give[resource]);
        }

        Log(SettlersLogKind.Discarded, seat, value: State.Discard[seat]);
        State.Discard[seat] = 0;
        FinishDiscards();
        return true;
    }

    private void FinishDiscards()
    {
        foreach (var owed in State.Discard)
        {
            if (owed > 0)
                return;
        }

        State.Phase = SettlersPhase.MovePirates;
    }

    private bool MovePirates(int tile)
    {
        if (State.Phase != SettlersPhase.MovePirates || tile < 0 || tile >= SettlersBoard.TileCount || tile == State.Pirates)
            return false;

        State.Pirates = tile;
        Log(SettlersLogKind.MovedPirates, State.Turn);
        var victims = State.Victims(State.Turn, tile);
        switch (victims.Count)
        {
            case 0:
                AfterRobbery();
                break;
            case 1:
                Rob(victims[0]);
                break;
            default:
                State.Phase = SettlersPhase.Steal;
                break;
        }

        return true;
    }

    private bool Steal(int victim)
    {
        if (State.Phase != SettlersPhase.Steal || !State.Victims(State.Turn, State.Pirates).Contains(victim))
            return false;

        Rob(victim);
        return true;
    }

    /// <summary>Takes one card at random from the victim's hand for the player whose turn it is.</summary>
    private void Rob(int victim)
    {
        if (TakeRandom(victim) is { } resource)
        {
            State.Players[State.Turn].Hand[resource]++;
            Log(SettlersLogKind.Stole, State.Turn, victim);
        }

        AfterRobbery();
    }

    private int? TakeRandom(int seat)
    {
        var hand = State.Players[seat].Hand;
        var total = SettlersRules.Total(hand);
        if (total == 0)
            return null;

        var pick = _random.Next(total);
        for (var resource = 0; resource < hand.Length; resource++)
        {
            pick -= hand[resource];
            if (pick >= 0)
                continue;

            hand[resource]--;
            return resource;
        }

        return null;
    }

    private void AfterRobbery()
    {
        State.Phase = State.Rolled ? SettlersPhase.Main : SettlersPhase.Roll;
    }

    private bool BankTrade(int give, int get)
    {
        var seat = State.Turn;
        if (State.Phase != SettlersPhase.Main || !IsResource(give) || !IsResource(get) || give == get || State.Bank[get] < 1)
            return false;

        var ratio = State.TradeRatio(seat, give);
        if (State.Players[seat].Hand[give] < ratio)
            return false;

        Give(seat, give, -ratio);
        Give(seat, get, 1);
        Log(SettlersLogKind.BankTrade, seat, give, get);
        return true;
    }

    /// <summary>Puts a trade to the table while the dice are rolled, or withdraws it when it is empty.</summary>
    private bool Offer(int seat, int[] give, int[] want)
    {
        if (State.Phase != SettlersPhase.Main)
            return false;

        if (SettlersRules.Total(give) == 0 && SettlersRules.Total(want) == 0)
        {
            var had = State.Offers[seat] != null;
            State.Offers[seat] = null;
            return had;
        }

        if (!ValidCounts(give) || !ValidCounts(want) || SettlersRules.Total(give) == 0 || SettlersRules.Total(want) == 0
            || !State.CanAfford(seat, give))
        {
            return false;
        }

        var offer = new SettlersOffer();
        Array.Copy(give, offer.Give, give.Length);
        Array.Copy(want, offer.Want, want.Length);
        State.Offers[seat] = offer;
        return true;
    }

    /// <summary>
    /// Takes an offer on the terms the player saw, so an offer changed in the meantime is refused. Every trade has
    /// the player whose turn it is on one side.
    /// </summary>
    private bool Accept(int seat, int offerer, int[] give, int[] want)
    {
        if (State.Phase != SettlersPhase.Main || offerer == seat || !State.Seated(offerer) || State.Offers[offerer] is not { } offer)
            return false;

        if (!SameCounts(give, offer.Give) || !SameCounts(want, offer.Want))
            return false;

        if (offerer != State.Turn && seat != State.Turn)
            return false;

        if (!State.CanAfford(offerer, offer.Give) || !State.CanAfford(seat, offer.Want))
            return false;

        for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
        {
            Move(offerer, seat, resource, offer.Give[resource]);
            Move(seat, offerer, resource, offer.Want[resource]);
        }

        State.Offers[offerer] = null;
        Log(SettlersLogKind.Traded, offerer, seat);
        return true;
    }

    private void Move(int from, int to, int resource, int amount)
    {
        State.Players[from].Hand[resource] -= amount;
        State.Players[to].Hand[resource] += amount;
    }

    private bool EndTurn()
    {
        var player = State.Players[State.Turn];
        player.Cards.AddRange(player.NewCards);
        player.NewCards.Clear();
        Array.Clear(State.Offers);
        BeginTurn(State.NextSeat(State.Turn));
        return true;
    }

    private void BeginTurn(int seat)
    {
        State.Turn = seat;
        State.Phase = SettlersPhase.Roll;
        State.Rolled = false;
        State.PlayedCard = false;
        State.FreeLanes = 0;

        // A route can change hands on someone else's turn; it wins on the holder's own.
        CheckWin();
    }

    /// <summary>The player whose turn it is wins on reaching the target.</summary>
    private void CheckWin()
    {
        if (!State.Running || State.Points(State.Turn) < SettlersRules.PointsToWin)
            return;

        State.Winner = State.Turn;
        State.Phase = SettlersPhase.Finished;
        Log(SettlersLogKind.Won, State.Turn);
    }

    /// <summary>
    /// Recounts every route. The bonus moves only to a route longer than the holder's; if the holder's is cut
    /// below another, the single longest takes it, and a tie for longest leaves it with nobody.
    /// </summary>
    private void UpdateRoute()
    {
        var max = 0;
        for (var seat = 0; seat < State.Players.Length; seat++)
        {
            var player = State.Players[seat];
            player.Route = player.InGame ? State.LongestRoute(seat) : 0;
            max = Math.Max(max, player.Route);
        }

        if (max < SettlersRules.MinRoute)
        {
            State.RouteHolder = -1;
            return;
        }

        if (State.RouteHolder >= 0 && State.Players[State.RouteHolder].Route == max)
            return;

        var holder = -1;
        for (var seat = 0; seat < State.Players.Length; seat++)
        {
            if (State.Players[seat].Route != max)
                continue;

            if (holder >= 0)
            {
                State.RouteHolder = -1;
                return;
            }

            holder = seat;
        }

        State.RouteHolder = holder;
    }

    /// <summary>
    /// Plays out what absent players owe: their discards, and their whole turn when it comes up. Waits while
    /// nobody in the game is seated, so a table that empties keeps its game.
    /// </summary>
    private void Autoplay()
    {
        if (!State.Running)
            return;

        var anyone = false;
        foreach (var player in State.Players)
        {
            anyone |= player.InGame && player.Present;
        }

        if (!anyone)
            return;

        for (var guard = 0; guard < 64 && State.Running; guard++)
        {
            if (State.Phase == SettlersPhase.Discard)
            {
                for (var seat = 0; seat < State.Players.Length; seat++)
                {
                    if (State.Discard[seat] <= 0 || State.Players[seat].Present)
                        continue;

                    for (var i = 0; i < State.Discard[seat]; i++)
                    {
                        if (TakeRandom(seat) is { } resource)
                            State.Bank[resource]++;
                    }

                    Log(SettlersLogKind.Discarded, seat, value: State.Discard[seat]);
                    State.Discard[seat] = 0;
                }

                FinishDiscards();
            }

            var turn = State.Turn;
            if (State.Players[turn].Present)
                return;

            switch (State.Phase)
            {
                // An empty seat still gets its setup pieces, so whoever takes it later can build from them.
                case SettlersPhase.SetupOutpost:
                    if (!BuildAtRandom(SettlersBoard.VertexCount, v => State.CanPlaceOutpost(turn, v), BuildOutpost))
                    {
                        Log(SettlersLogKind.Skipped, turn);
                        AdvanceSetup();
                    }

                    break;

                case SettlersPhase.SetupLane:
                    if (!BuildAtRandom(SettlersBoard.EdgeCount, e => State.CanPlaceLane(turn, e), BuildLane))
                    {
                        Log(SettlersLogKind.Skipped, turn);
                        AdvanceSetup();
                    }

                    break;

                case SettlersPhase.Roll:
                case SettlersPhase.Main:
                case SettlersPhase.Survey:
                    Log(SettlersLogKind.Skipped, turn);
                    EndTurn();
                    break;

                case SettlersPhase.MovePirates:
                    var tile = _random.Next(SettlersBoard.TileCount - 1);
                    MovePirates(tile >= State.Pirates ? tile + 1 : tile);
                    break;

                case SettlersPhase.Steal:
                    var victims = State.Victims(turn, State.Pirates);
                    if (victims.Count == 0)
                        AfterRobbery();
                    else
                        Rob(victims[_random.Next(victims.Count)]);
                    break;

                default:
                    return;
            }
        }
    }

    /// <summary>Builds at a random spot that <paramref name="legal"/> allows; false if there is none.</summary>
    private bool BuildAtRandom(int count, Func<int, bool> legal, Func<int, bool> build)
    {
        var spots = new List<int>();
        for (var spot = 0; spot < count; spot++)
        {
            if (legal(spot))
                spots.Add(spot);
        }

        return spots.Count > 0 && build(spots[_random.Next(spots.Count)]);
    }

    private static bool IsResource(int resource)
    {
        return resource >= 0 && resource < SettlersRules.ResourceCount;
    }

    private static bool SameCounts(int[] a, int[] b)
    {
        if (a.Length != b.Length)
            return false;

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private static bool ValidCounts(int[] counts)
    {
        if (counts.Length != SettlersRules.ResourceCount)
            return false;

        foreach (var count in counts)
        {
            if (count < 0)
                return false;
        }

        return true;
    }

    private void Log(SettlersLogKind kind, int seat, int other = -1, int value = 0)
    {
        State.Log.Add(new SettlersLogEntry(kind, seat, other, value));
        if (State.Log.Count > SettlersRules.LogLength)
            State.Log.RemoveAt(0);
    }
}
