namespace Content.Shared._WG.ScratchTickets;

/// <summary>How a scratch ticket is laid out and what wins on it.</summary>
public enum WGScratchFormat : byte
{
    /// <summary>A three by three grid: three of one symbol anywhere win.</summary>
    MatchThree,

    /// <summary>One winning symbol over six of the player's: finding it among the six wins.</summary>
    WinningSymbol,

    /// <summary>Three games of two symbols: a game with both alike wins.</summary>
    Pairs,
}

/// <summary>The scratch tickets' layouts and how a ticket is rolled.</summary>
public static class WGScratchTicketRules
{
    /// <summary>Columns of the <see cref="WGScratchFormat.MatchThree"/> grid and of the player's symbols under a winning symbol.</summary>
    public const int Columns = 3;

    /// <summary>The player's symbols on a <see cref="WGScratchFormat.WinningSymbol"/> ticket, after the winning one in cell 0.</summary>
    public const int OwnSymbols = 6;

    /// <summary>Games on a <see cref="WGScratchFormat.Pairs"/> ticket, two cells each.</summary>
    public const int Games = 3;

    /// <summary>The share of the foil that has to come off before the ticket counts as scratched.</summary>
    public const float RevealFraction = 0.9f;

    public static int CellCount(WGScratchFormat format)
    {
        return format switch
        {
            WGScratchFormat.WinningSymbol => 1 + OwnSymbols,
            WGScratchFormat.Pairs => Games * 2,
            _ => Columns * Columns,
        };
    }

    /// <summary>How many of one symbol the player has to find to win.</summary>
    public static int Match(WGScratchFormat format)
    {
        return format switch
        {
            WGScratchFormat.WinningSymbol => 1,
            WGScratchFormat.Pairs => 2,
            _ => 3,
        };
    }

    /// <summary>
    /// Fills <paramref name="cells"/> and returns the winning symbol, or -1. The cells show the winner's match once
    /// and no other, so no ticket wins by accident.
    /// </summary>
    public static int Roll(WGScratchFormat format, IReadOnlyList<WGScratchSymbol> symbols, float loseWeight, System.Random random, List<int> cells)
    {
        cells.Clear();

        var total = loseWeight;
        foreach (var symbol in symbols)
        {
            total += symbol.Weight;
        }

        var winner = -1;
        var roll = random.NextDouble() * total - loseWeight;
        for (var i = 0; i < symbols.Count && roll >= 0 && winner < 0; i++)
        {
            roll -= symbols[i].Weight;
            if (roll < 0 && symbols[i].Weight > 0)
                winner = i;
        }

        switch (format)
        {
            case WGScratchFormat.WinningSymbol:
                FillWinningSymbol(symbols, winner, random, cells);
                break;
            case WGScratchFormat.Pairs:
                FillPairs(symbols.Count, winner, random, cells);
                break;
            default:
                FillMatchThree(symbols.Count, winner, random, cells);
                break;
        }

        return winner;
    }

    /// <summary>The symbol the cells win on, or -1.</summary>
    public static int Winner(WGScratchFormat format, IReadOnlyList<int> cells)
    {
        switch (format)
        {
            case WGScratchFormat.WinningSymbol:
                for (var i = 1; i < cells.Count; i++)
                {
                    if (cells[i] == cells[0])
                        return cells[0];
                }

                return -1;

            case WGScratchFormat.Pairs:
                for (var i = 0; i + 1 < cells.Count; i += 2)
                {
                    if (cells[i] == cells[i + 1])
                        return cells[i];
                }

                return -1;
        }

        foreach (var candidate in cells)
        {
            var count = 0;
            foreach (var cell in cells)
            {
                if (cell == candidate)
                    count++;
            }

            if (count >= Match(format))
                return candidate;
        }

        return -1;
    }

    /// <summary>Whether the cell at <paramref name="index"/> is part of the winning match.</summary>
    public static bool Wins(WGScratchFormat format, IReadOnlyList<int> cells, int index)
    {
        if (index < 0 || index >= cells.Count)
            return false;

        // Another game may show one of the winning pair's symbols without winning.
        if (format == WGScratchFormat.Pairs)
            return (index ^ 1) < cells.Count && cells[index] == cells[index ^ 1];

        return cells[index] == Winner(format, cells);
    }

    private static void FillMatchThree(int symbols, int winner, System.Random random, List<int> cells)
    {
        if (winner >= 0)
        {
            for (var i = 0; i < Match(WGScratchFormat.MatchThree); i++)
            {
                cells.Add(winner);
            }
        }

        Take(Filler(symbols, winner, random), CellCount(WGScratchFormat.MatchThree) - cells.Count, cells);
        Shuffle(cells, random);
    }

    private static void FillWinningSymbol(IReadOnlyList<WGScratchSymbol> symbols, int winner, System.Random random, List<int> cells)
    {
        // A losing ticket still shows a symbol worth winning.
        var key = winner;
        if (key < 0)
        {
            var prizes = new List<int>();
            for (var i = 0; i < symbols.Count; i++)
            {
                if (symbols[i].Prize > 0)
                    prizes.Add(i);
            }

            key = prizes.Count > 0 ? prizes[random.Next(prizes.Count)] : random.Next(symbols.Count);
        }

        var own = new List<int>();
        if (winner >= 0)
            own.Add(winner);

        Take(Filler(symbols.Count, key, random), OwnSymbols - own.Count, own);
        Shuffle(own, random);

        cells.Add(key);
        cells.AddRange(own);
    }

    private static void FillPairs(int symbols, int winner, System.Random random, List<int> cells)
    {
        var winningGame = winner < 0 ? -1 : random.Next(Games);
        for (var game = 0; game < Games; game++)
        {
            var first = game == winningGame ? winner : random.Next(symbols);
            var second = game == winningGame || symbols < 2 ? first : (first + 1 + random.Next(symbols - 1)) % symbols;
            cells.Add(first);
            cells.Add(second);
        }
    }

    /// <summary>Every symbol but <paramref name="except"/> twice over, shuffled.</summary>
    private static List<int> Filler(int symbols, int except, System.Random random)
    {
        var filler = new List<int>();
        for (var i = 0; i < symbols; i++)
        {
            if (i == except)
                continue;

            filler.Add(i);
            filler.Add(i);
        }

        Shuffle(filler, random);
        return filler;
    }

    private static void Take(List<int> from, int count, List<int> into)
    {
        for (var i = 0; i < count && i < from.Count; i++)
        {
            into.Add(from[i]);
        }
    }

    private static void Shuffle(List<int> list, System.Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
