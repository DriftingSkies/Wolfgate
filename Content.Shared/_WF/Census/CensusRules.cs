namespace Content.Shared._WF.Census;

/// <summary>The limits a census and its answers keep to, as the SSymphony panel's contract sets them.</summary>
public static class CensusRules
{
    public const int MaxTitle = 120;
    public const int MaxDescription = 2000;
    public const int MaxQuestionsBytes = 64 * 1024;
    public const int MinQuestions = 1;
    public const int MaxQuestions = 30;
    public const int MaxQuestionId = 24;
    public const int MaxPrompt = 300;
    public const int MinOptions = 2;
    public const int MaxOptions = 20;
    public const int MaxOption = 100;
    public const int MaxTextLength = 2000;
    public const int DefaultTextLength = 500;

    /// <summary>Submissions one player may send in <see cref="SubmitWindow"/>.</summary>
    public const int SubmitsPerWindow = 4;
    public static readonly TimeSpan SubmitWindow = TimeSpan.FromMinutes(1);

    public const string StateDraft = "draft";
    public const string StateOpen = "open";
    public const string StateClosed = "closed";

    /// <summary>Whether a question id is 1 to 24 of a-z, 0-9 and _.</summary>
    public static bool ValidId(string id)
    {
        if (id.Length is 0 or > MaxQuestionId)
            return false;

        foreach (var c in id)
        {
            if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                return false;
        }

        return true;
    }

    /// <summary>Fewest options a multi answer needs: its min, and at least one when the question is required.</summary>
    public static int LeastPicks(CensusQuestion question)
    {
        return question.Required ? Math.Max(1, question.Min) : question.Min;
    }
}
