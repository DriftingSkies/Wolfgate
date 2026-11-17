using System.Text;

namespace Content.Shared._WG.TestDummies;

/// <summary>Builds the one-line description of an asked player, for the admin window and the console list.</summary>
public static class WGTestDummyFormat
{
    /// <summary>"Player as Character (Ship) - disconnected", leaving out the parts the entry lacks.</summary>
    public static string Describe(WGTestDummyEntry entry)
    {
        var sb = new StringBuilder(entry.PlayerName);
        if (entry.CharacterName.Length > 0)
            sb.Append(' ').Append(Loc.GetString("wg-test-dummy-row-as", ("character", entry.CharacterName)));
        if (entry.ShipName.Length > 0)
            sb.Append(' ').Append(Loc.GetString("wg-test-dummy-row-ship", ("ship", entry.ShipName)));
        if (!entry.Connected)
            sb.Append(' ').Append(Loc.GetString("wg-test-dummy-row-disconnected"));
        return sb.ToString();
    }

    /// <summary>The loc key naming an answer.</summary>
    public static string AnswerKey(WGTestDummyAnswer answer)
    {
        return answer switch
        {
            WGTestDummyAnswer.OptedIn => "wg-test-dummy-answer-opted-in",
            WGTestDummyAnswer.OptedOut => "wg-test-dummy-answer-opted-out",
            _ => "wg-test-dummy-answer-pending",
        };
    }
}
