using System.Text;

namespace Content.Shared._WF.TestDummies;

/// <summary>Builds the one-line description of an asked player, for the admin window and the console list.</summary>
public static class WFTestDummyFormat
{
    /// <summary>"Player as Character (Ship) - disconnected", leaving out the parts the entry lacks.</summary>
    public static string Describe(WFTestDummyEntry entry)
    {
        var sb = new StringBuilder(entry.PlayerName);
        if (entry.CharacterName.Length > 0)
            sb.Append(' ').Append(Loc.GetString("wf-test-dummy-row-as", ("character", entry.CharacterName)));
        if (entry.ShipName.Length > 0)
            sb.Append(' ').Append(Loc.GetString("wf-test-dummy-row-ship", ("ship", entry.ShipName)));
        if (!entry.Connected)
            sb.Append(' ').Append(Loc.GetString("wf-test-dummy-row-disconnected"));
        return sb.ToString();
    }

    /// <summary>The loc key naming an answer.</summary>
    public static string AnswerKey(WFTestDummyAnswer answer)
    {
        return answer switch
        {
            WFTestDummyAnswer.OptedIn => "wf-test-dummy-answer-opted-in",
            WFTestDummyAnswer.OptedOut => "wf-test-dummy-answer-opted-out",
            _ => "wf-test-dummy-answer-pending",
        };
    }
}
