namespace Content.Shared._WG.Minigames;

/// <summary>
/// Riddles: answer a run of riddles, each with four answers to pick from. One wrong answer loses. Riddle text lives in
/// Fluent as <c>wg-minigame-riddle-N-question</c>, <c>-answer</c> and <c>-wrong-1</c> to <c>-wrong-3</c>.
/// Move: Action = the answer picked, 0 to 3.
/// </summary>
public sealed class RiddleGame : MinigameGame
{
    public const int Answers = 4;

    /// <summary>Riddle numbers in the order they are asked.</summary>
    public readonly List<int> Riddles = new();

    /// <summary>Fluent ids of the current riddle's answers, in the order shown.</summary>
    public readonly List<string> Shown = new();

    public int Current { get; private set; }

    /// <summary>Where the right answer sits in <see cref="Shown"/>.</summary>
    public int Correct { get; private set; }

    public override MinigameKind Kind => MinigameKind.Riddles;

    private int Total => MinigameRules.RiddlesToWin(Difficulty);

    public RiddleGame(MinigameDifficulty difficulty, System.Random random) : base(difficulty, random)
    {
        var pool = new List<int>();
        for (var i = 1; i <= MinigameRules.RiddleCount; i++)
            pool.Add(i);

        for (var i = 0; i < Total; i++)
        {
            var pick = random.Next(pool.Count);
            Riddles.Add(pool[pick]);
            pool.RemoveAt(pick);
        }

        Deal();
        Say("wg-minigame-riddle-start", ("count", Total));
    }

    public static string Question(int riddle) => $"wg-minigame-riddle-{riddle}-question";

    private void Deal()
    {
        var riddle = Riddles[Current];
        Shown.Clear();
        Shown.Add($"wg-minigame-riddle-{riddle}-answer");
        for (var i = 1; i < Answers; i++)
            Shown.Add($"wg-minigame-riddle-{riddle}-wrong-{i}");

        for (var i = Shown.Count - 1; i > 0; i--)
        {
            var j = Random.Next(i + 1);
            (Shown[i], Shown[j]) = (Shown[j], Shown[i]);
        }

        Correct = Shown.IndexOf($"wg-minigame-riddle-{riddle}-answer");
    }

    protected override bool ApplyPlayerMove(MinigameMove move)
    {
        if (move.Action < 0 || move.Action >= Answers)
            return false;

        if (move.Action != Correct)
        {
            Status = MinigameStatus.Lost;
            Say("wg-minigame-riddle-wrong");
            return true;
        }

        Current++;
        if (Current >= Total)
        {
            Status = MinigameStatus.Won;
            Say("wg-minigame-riddle-won");
            return true;
        }

        Deal();
        Say("wg-minigame-riddle-right", ("left", Total - Current));
        return true;
    }

    /// <summary>Fluent ids; the server localizes them before sending, so the ids never reach the client.</summary>
    public override MinigameView GetView()
    {
        var riddle = Riddles[Math.Min(Current, Total - 1)];
        return new RiddleView(Question(riddle), new List<string>(Shown), Math.Min(Current + 1, Total), Total);
    }
}
