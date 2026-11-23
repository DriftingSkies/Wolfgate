namespace Content.Client._WG.JoinQueue;

/// <summary>Number formatting for the queue screen.</summary>
public static class JoinQueueText
{
    /// <summary>Seconds as <c>MM:SS</c>, or <c>H:MM:SS</c> past an hour.</summary>
    public static string Clock(int seconds)
    {
        seconds = Math.Max(0, seconds);
        var minutes = seconds / 60;
        return minutes >= 60
            ? $"{minutes / 60}:{minutes % 60:00}:{seconds % 60:00}"
            : $"{minutes:00}:{seconds % 60:00}";
    }

    /// <summary>Whole minutes left, rounded up; 0 means under a minute.</summary>
    public static int MinutesLeft(int seconds)
    {
        return seconds < 60 ? 0 : (seconds + 59) / 60;
    }
}
