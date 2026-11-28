using Robust.Shared.Serialization;

namespace Content.Shared._WG.Settlers;

/// <summary>One seat's player: their hand, contracts and what they have played.</summary>
[Serializable, NetSerializable]
public sealed class SettlersPlayer
{
    /// <summary>The seat was taken when the game started and plays in it.</summary>
    public bool InGame;

    /// <summary>Someone sits in the seat. An absent player's turns and choices are played out for them.</summary>
    public bool Present;

    public int[] Hand = new int[SettlersRules.ResourceCount];

    /// <summary>Contracts that may be played.</summary>
    public List<SettlersCard> Cards = new();

    /// <summary>Contracts bought this turn, which wait for the next.</summary>
    public List<SettlersCard> NewCards = new();

    /// <summary>Escorts played, for the largest fleet.</summary>
    public int Escorts;

    /// <summary>The player's longest unbroken run of lanes.</summary>
    public int Route;

    /// <summary>Sizes shown to everyone while the hand and contracts themselves are hidden.</summary>
    public int HandCount;
    public int CardCount;

    /// <summary>Charters held. Only the player's own copy of the state carries them.</summary>
    public int HiddenPoints;

    public SettlersPlayer Copy(bool hidden)
    {
        var copy = new SettlersPlayer
        {
            InGame = InGame,
            Present = Present,
            Escorts = Escorts,
            Route = Route,
            HandCount = SettlersRules.Total(Hand),
            CardCount = Cards.Count + NewCards.Count,
        };

        if (hidden)
            return copy;

        Array.Copy(Hand, copy.Hand, Hand.Length);
        copy.Cards.AddRange(Cards);
        copy.NewCards.AddRange(NewCards);
        copy.HiddenPoints = Charters();
        return copy;
    }

    public int Charters()
    {
        var count = 0;
        foreach (var card in Cards)
        {
            if (card == SettlersCard.Charter)
                count++;
        }

        foreach (var card in NewCards)
        {
            if (card == SettlersCard.Charter)
                count++;
        }

        return count;
    }
}

/// <summary>A trade a player puts to the table.</summary>
[Serializable, NetSerializable]
public sealed class SettlersOffer
{
    public int[] Give = new int[SettlersRules.ResourceCount];
    public int[] Want = new int[SettlersRules.ResourceCount];
}

public enum SettlersLogKind : byte
{
    Started,
    Rolled,
    Built,
    Upgraded,
    BoughtCard,
    PlayedCard,
    MovedPirates,
    Stole,
    Discarded,
    BankTrade,
    Traded,
    Skipped,
    Won,
}

/// <summary>A line of the game log: who did what, with one or two numbers whose meaning depends on the kind.</summary>
[Serializable, NetSerializable]
public struct SettlersLogEntry
{
    public SettlersLogKind Kind;
    public int Seat;
    public int Other;
    public int Value;

    public SettlersLogEntry(SettlersLogKind kind, int seat, int other = -1, int value = 0)
    {
        Kind = kind;
        Seat = seat;
        Other = other;
        Value = value;
    }
}

/// <summary>
/// Everything about a game of Settlers of Colossus that a player may see. The server holds the full state; each
/// viewer is sent a copy with everyone else's hand and contracts left out. The rule checks here run on both sides,
/// so the client can light up the legal moves.
/// </summary>
[Serializable, NetSerializable]
public sealed class SettlersState
{
    public SettlersSector[] Sectors = new SettlersSector[SettlersBoard.TileCount];

    /// <summary>The number on each tile, 0 on the dead zone.</summary>
    public int[] Numbers = new int[SettlersBoard.TileCount];

    /// <summary>What each dock trades: a resource, or <see cref="SettlersRules.AnyDock"/>.</summary>
    public int[] Docks = new int[SettlersBoard.DockCount];

    /// <summary>The tile the pirates sit on.</summary>
    public int Pirates;

    /// <summary>The seat owning each corner and side, or -1.</summary>
    public int[] VertexOwner = NewOwners(SettlersBoard.VertexCount);
    public bool[] Colony = new bool[SettlersBoard.VertexCount];
    public int[] EdgeOwner = NewOwners(SettlersBoard.EdgeCount);

    public SettlersPlayer[] Players = NewPlayers();

    public SettlersPhase Phase;
    public int Turn;

    /// <summary>The seats in setup order, forwards and back again, and how far through it the game is.</summary>
    public int[] SetupOrder = Array.Empty<int>();
    public int SetupStep;

    /// <summary>The outpost just placed in setup, which its lane must touch.</summary>
    public int SetupOutpost = -1;

    public bool Rolled;
    public int Die1;
    public int Die2;

    /// <summary>Counts rolls, so a client can tell a new one.</summary>
    public int RollCount;

    /// <summary>A contract was played this turn.</summary>
    public bool PlayedCard;

    public int FreeLanes;

    public int[] Bank = new int[SettlersRules.ResourceCount];
    public int DeckCount;

    public int RouteHolder = -1;
    public int FleetHolder = -1;
    public int Winner = -1;

    /// <summary>Each seat's open trade offer, if any.</summary>
    public SettlersOffer?[] Offers = new SettlersOffer?[SettlersRules.MaxPlayers];

    /// <summary>How many cards each seat still owes after a 7.</summary>
    public int[] Discard = new int[SettlersRules.MaxPlayers];

    public List<SettlersLogEntry> Log = new();

    private static int[] NewOwners(int count)
    {
        var owners = new int[count];
        Array.Fill(owners, -1);
        return owners;
    }

    private static SettlersPlayer[] NewPlayers()
    {
        var players = new SettlersPlayer[SettlersRules.MaxPlayers];
        for (var i = 0; i < players.Length; i++)
        {
            players[i] = new SettlersPlayer();
        }

        return players;
    }

    /// <summary>A copy for one viewer: only their own seat's hand and contracts are filled in.</summary>
    public SettlersState CopyFor(int? viewer)
    {
        var copy = new SettlersState
        {
            Pirates = Pirates,
            Phase = Phase,
            Turn = Turn,
            SetupOrder = (int[]) SetupOrder.Clone(),
            SetupStep = SetupStep,
            SetupOutpost = SetupOutpost,
            Rolled = Rolled,
            Die1 = Die1,
            Die2 = Die2,
            RollCount = RollCount,
            PlayedCard = PlayedCard,
            FreeLanes = FreeLanes,
            DeckCount = DeckCount,
            RouteHolder = RouteHolder,
            FleetHolder = FleetHolder,
            Winner = Winner,
        };

        Array.Copy(Sectors, copy.Sectors, Sectors.Length);
        Array.Copy(Numbers, copy.Numbers, Numbers.Length);
        Array.Copy(Docks, copy.Docks, Docks.Length);
        Array.Copy(VertexOwner, copy.VertexOwner, VertexOwner.Length);
        Array.Copy(Colony, copy.Colony, Colony.Length);
        Array.Copy(EdgeOwner, copy.EdgeOwner, EdgeOwner.Length);
        // Only whether the bank has a resource: exact counts would give away the other hands.
        for (var resource = 0; resource < Bank.Length; resource++)
        {
            copy.Bank[resource] = Math.Min(Bank[resource], 1);
        }

        Array.Copy(Discard, copy.Discard, Discard.Length);
        copy.Log.AddRange(Log);

        for (var seat = 0; seat < Players.Length; seat++)
        {
            copy.Players[seat] = Players[seat].Copy(seat != viewer);
            if (Offers[seat] is not { } offer)
                continue;

            var offerCopy = new SettlersOffer();
            Array.Copy(offer.Give, offerCopy.Give, offer.Give.Length);
            Array.Copy(offer.Want, offerCopy.Want, offer.Want.Length);
            copy.Offers[seat] = offerCopy;
        }

        return copy;
    }

    public bool Running => Phase is not (SettlersPhase.Lobby or SettlersPhase.Finished);

    public bool Seated(int seat)
    {
        return seat >= 0 && seat < Players.Length && Players[seat].InGame;
    }

    public bool CanAfford(int seat, int[] cost)
    {
        var hand = Players[seat].Hand;
        for (var i = 0; i < cost.Length; i++)
        {
            if (hand[i] < cost[i])
                return false;
        }

        return true;
    }

    public int Count(int seat, bool colonies)
    {
        var count = 0;
        for (var v = 0; v < VertexOwner.Length; v++)
        {
            if (VertexOwner[v] == seat && Colony[v] == colonies)
                count++;
        }

        return count;
    }

    public int Lanes(int seat)
    {
        var count = 0;
        foreach (var owner in EdgeOwner)
        {
            if (owner == seat)
                count++;
        }

        return count;
    }

    /// <summary>The corner is empty and none of its neighbours holds a building.</summary>
    public bool SpotFree(int vertex)
    {
        if (VertexOwner[vertex] >= 0)
            return false;

        foreach (var neighbour in SettlersBoard.VertexNeighbours[vertex])
        {
            if (VertexOwner[neighbour] >= 0)
                return false;
        }

        return true;
    }

    public bool CanPlaceOutpost(int seat, int vertex)
    {
        if (vertex < 0 || vertex >= SettlersBoard.VertexCount || !SpotFree(vertex))
            return false;

        if (Phase == SettlersPhase.SetupOutpost)
            return true;

        if (Count(seat, false) >= SettlersRules.MaxOutposts)
            return false;

        foreach (var edge in SettlersBoard.VertexEdges[vertex])
        {
            if (EdgeOwner[edge] == seat)
                return true;
        }

        return false;
    }

    public bool CanPlaceColony(int seat, int vertex)
    {
        return vertex >= 0 && vertex < SettlersBoard.VertexCount && VertexOwner[vertex] == seat && !Colony[vertex]
               && Count(seat, true) < SettlersRules.MaxColonies;
    }

    /// <summary>The side is free and joins the player's network at a corner no rival has built on.</summary>
    public bool CanPlaceLane(int seat, int edge)
    {
        if (edge < 0 || edge >= SettlersBoard.EdgeCount || EdgeOwner[edge] >= 0 || Lanes(seat) >= SettlersRules.MaxLanes)
            return false;

        if (Phase == SettlersPhase.SetupLane)
            return SettlersBoard.Touches(edge, SetupOutpost);

        return Joins(seat, edge, SettlersBoard.EdgeA[edge]) || Joins(seat, edge, SettlersBoard.EdgeB[edge]);
    }

    private bool Joins(int seat, int edge, int vertex)
    {
        if (VertexOwner[vertex] == seat)
            return true;

        if (VertexOwner[vertex] >= 0)
            return false;

        foreach (var other in SettlersBoard.VertexEdges[vertex])
        {
            if (other != edge && EdgeOwner[other] == seat)
                return true;
        }

        return false;
    }

    public bool AnyLane(int seat)
    {
        for (var edge = 0; edge < SettlersBoard.EdgeCount; edge++)
        {
            if (CanPlaceLane(seat, edge))
                return true;
        }

        return false;
    }

    /// <summary>The players, other than the robber, with a building on the tile and a card to steal.</summary>
    public List<int> Victims(int robber, int tile)
    {
        var victims = new List<int>();
        foreach (var vertex in SettlersBoard.TileVertices[tile])
        {
            var owner = VertexOwner[vertex];
            if (owner >= 0 && owner != robber && !victims.Contains(owner) && HandSize(owner) > 0)
                victims.Add(owner);
        }

        return victims;
    }

    /// <summary>How many cards the player holds: counted on the server, read from the shown size on a client.</summary>
    public int HandSize(int seat)
    {
        var player = Players[seat];
        return Math.Max(player.HandCount, SettlersRules.Total(player.Hand));
    }

    /// <summary>How many of a resource the player gives the bank for one of another.</summary>
    public int TradeRatio(int seat, int resource)
    {
        var ratio = SettlersRules.BankRatio;
        for (var dock = 0; dock < Docks.Length; dock++)
        {
            var edge = SettlersBoard.DockEdges[dock];
            if (VertexOwner[SettlersBoard.EdgeA[edge]] != seat && VertexOwner[SettlersBoard.EdgeB[edge]] != seat)
                continue;

            if (Docks[dock] == resource)
                ratio = Math.Min(ratio, SettlersRules.ResourceDockRatio);
            else if (Docks[dock] == SettlersRules.AnyDock)
                ratio = Math.Min(ratio, SettlersRules.AnyDockRatio);
        }

        return ratio;
    }

    /// <summary>The points everyone can see: buildings and the two bonuses.</summary>
    public int PublicPoints(int seat)
    {
        var points = Count(seat, false) + 2 * Count(seat, true);
        if (RouteHolder == seat)
            points += SettlersRules.RoutePoints;
        if (FleetHolder == seat)
            points += SettlersRules.FleetPoints;
        return points;
    }

    /// <summary>All the player's points, charters included. Only right in the player's own copy, or on the server.</summary>
    public int Points(int seat)
    {
        var player = Players[seat];
        return PublicPoints(seat) + Math.Max(player.HiddenPoints, player.Charters());
    }

    /// <summary>The longest trail of the player's lanes that no rival's building cuts.</summary>
    public int LongestRoute(int seat)
    {
        var used = new bool[SettlersBoard.EdgeCount];
        var best = 0;
        for (var vertex = 0; vertex < SettlersBoard.VertexCount; vertex++)
        {
            best = Math.Max(best, Walk(seat, vertex, used, true));
        }

        return best;
    }

    private int Walk(int seat, int vertex, bool[] used, bool start)
    {
        if (!start && VertexOwner[vertex] >= 0 && VertexOwner[vertex] != seat)
            return 0;

        var best = 0;
        foreach (var edge in SettlersBoard.VertexEdges[vertex])
        {
            if (used[edge] || EdgeOwner[edge] != seat)
                continue;

            used[edge] = true;
            best = Math.Max(best, 1 + Walk(seat, SettlersBoard.OtherEnd(edge, vertex), used, false));
            used[edge] = false;
        }

        return best;
    }

    /// <summary>The next seat in play after this one.</summary>
    public int NextSeat(int seat)
    {
        for (var i = 1; i <= Players.Length; i++)
        {
            var next = (seat + i) % Players.Length;
            if (Players[next].InGame)
                return next;
        }

        return seat;
    }
}
