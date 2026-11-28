namespace Content.Shared._WG.Settlers;

/// <summary>The five goods a sector yields. Hands and costs are arrays indexed by this.</summary>
public enum SettlersResource : byte
{
    Scrap,
    Ice,
    Gas,
    Biomass,
    Ore,
}

/// <summary>What a hex is. The first five yield the resource of the same number; the dead zone yields nothing.</summary>
public enum SettlersSector : byte
{
    Salvage,
    IceField,
    GasGiant,
    FarmWorld,
    OreBelt,
    DeadZone,
}

/// <summary>A contract card bought from the deck.</summary>
public enum SettlersCard : byte
{
    /// <summary>Move the pirates and rob a neighbour; three or more played can win the largest fleet.</summary>
    Escort,

    /// <summary>A hidden victory point.</summary>
    Charter,

    /// <summary>Lay two lanes for free.</summary>
    Survey,

    /// <summary>Take any two resources from the bank.</summary>
    Windfall,

    /// <summary>Every other player hands over all of one resource.</summary>
    Embargo,
}

public enum SettlersPhase : byte
{
    /// <summary>Players take seats; the game has not started.</summary>
    Lobby,
    SetupOutpost,
    SetupLane,

    /// <summary>The turn has started and the dice are not rolled. A contract may be played first.</summary>
    Roll,

    /// <summary>A 7 was rolled and players holding more than seven cards are discarding.</summary>
    Discard,
    MovePirates,
    Steal,

    /// <summary>The dice are rolled: build, trade, play a contract or end the turn.</summary>
    Main,

    /// <summary>A survey is being laid as free lanes.</summary>
    Survey,
    Finished,
}

/// <summary>Every number of Settlers of Colossus.</summary>
public static class SettlersRules
{
    public const int MaxPlayers = 4;

    /// <summary>A single player may start a game alone to learn it.</summary>
    public const int MinPlayers = 1;

    public const int ResourceCount = 5;
    public const int PointsToWin = 10;
    public const int BankStock = 19;

    public const int MaxLanes = 15;
    public const int MaxOutposts = 5;
    public const int MaxColonies = 4;

    /// <summary>A hand larger than this loses half when a 7 is rolled.</summary>
    public const int HandLimit = 7;

    public const int MinRoute = 5;
    public const int MinFleet = 3;
    public const int RoutePoints = 2;
    public const int FleetPoints = 2;
    public const int SurveyLanes = 2;
    public const int WindfallTake = 2;

    public const int BankRatio = 4;
    public const int AnyDockRatio = 3;
    public const int ResourceDockRatio = 2;

    /// <summary>A dock that trades any resource at <see cref="AnyDockRatio"/>.</summary>
    public const int AnyDock = -1;

    /// <summary>How many entries the game log keeps.</summary>
    public const int LogLength = 8;

    public static readonly int[] LaneCost = { 1, 1, 0, 0, 0 };
    public static readonly int[] OutpostCost = { 1, 1, 1, 1, 0 };
    public static readonly int[] ColonyCost = { 0, 0, 0, 2, 3 };
    public static readonly int[] CardCost = { 0, 0, 1, 1, 1 };

    /// <summary>How many of each sector the board holds, by <see cref="SettlersSector"/>.</summary>
    public static readonly int[] SectorCounts = { 4, 3, 4, 4, 3, 1 };

    /// <summary>The numbers laid on the producing sectors.</summary>
    public static readonly int[] Numbers = { 2, 3, 3, 4, 4, 5, 5, 6, 6, 8, 8, 9, 9, 10, 10, 11, 11, 12 };

    /// <summary>The docks round the coast: four that take anything, one for each resource.</summary>
    public static readonly int[] Docks = { AnyDock, AnyDock, AnyDock, AnyDock, 0, 1, 2, 3, 4 };

    /// <summary>The contract deck, by <see cref="SettlersCard"/>.</summary>
    public static readonly int[] CardCounts = { 14, 5, 2, 2, 2 };

    /// <summary>How many of the 36 rolls of two dice make this number, shown as pips under it.</summary>
    public static int Odds(int number)
    {
        return number is < 2 or > 12 or 7 ? 0 : 6 - Math.Abs(7 - number);
    }

    /// <summary>A 6 or an 8; no two sit side by side.</summary>
    public static bool IsHot(int number)
    {
        return number is 6 or 8;
    }

    /// <summary>The resource a sector yields, or null for the dead zone.</summary>
    public static SettlersResource? Yield(SettlersSector sector)
    {
        return sector == SettlersSector.DeadZone ? null : (SettlersResource) sector;
    }

    public static int Total(int[] counts)
    {
        var total = 0;
        foreach (var count in counts)
        {
            total += count;
        }

        return total;
    }
}
