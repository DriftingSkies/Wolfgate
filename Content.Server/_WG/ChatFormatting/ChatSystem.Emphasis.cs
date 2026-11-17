using System.Text.RegularExpressions;

namespace Content.Server.Chat.Systems;

public partial class ChatSystem
{
    // The atomic group gives every character one reading: "~" before a delimiter is an escape, anything else is
    // itself. Without it a run of tildes after an unclosed opener backtracks exponentially, and muffled whispers
    // are mostly tildes.
    private static readonly Regex Pattern = new(
        @"~([|+^])|([|+^])((?>~[|+^]|(?!\2).)+?)\2",
        RegexOptions.Compiled | RegexOptions.Singleline,
        // Player input on the game thread: one message must never hold the tick.
        TimeSpan.FromMilliseconds(100));

    /// <summary>Converts |italic|, +bold+ and ^small^ to BBCode; an input that times out comes back unchanged.</summary>
    public static string ConvertCustomEmphasisToBBCode(string message)
    {
        try
        {
            return Parse(message);
        }
        catch (RegexMatchTimeoutException)
        {
            return message;
        }
    }

    private static string Parse(string input)
    {
        return Pattern.Replace(input, m =>
        {
            // Escaped delimiter like ~+ → output bare character
            if (m.Groups[1].Success)
                return m.Groups[1].Value;

            var ch = m.Groups[2].Value[0];
            var tag = ch switch
            {
                '|' => "italic",
                '+' => "bold",
                '^' => "small",
                _   => null
            };

            string content = Parse(m.Groups[3].Value);

            // ^ uses [font size=8] for small text
            if (ch == '^')
                return $"[font size=8]{content}[/font]";
            return $"[{tag}]{content}[/{tag}]";
        });
    }
}
