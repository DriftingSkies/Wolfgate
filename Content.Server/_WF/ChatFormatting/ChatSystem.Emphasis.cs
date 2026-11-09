using System.Text.RegularExpressions;

namespace Content.Server.Chat.Systems;

public partial class ChatSystem
{
    private static readonly Regex Pattern = new(
        @"~([|+^])|([|+^])((?:~.|(?!\2).)+?)\2",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static string ConvertCustomEmphasisToBBCode(string message) => Parse(message);

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
