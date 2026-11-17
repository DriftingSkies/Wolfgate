#nullable enable
using System.Linq;
using Content.Server.Chat.Systems;

namespace Content.IntegrationTests.Tests._WG.ChatFormatting;

/// <summary>
///     Custom chat emphasis delimiters convert to BBCode tags for speech, emotes, whispers, and subtle emotes.
/// </summary>
[TestFixture]
[TestOf(typeof(ChatSystem))]
public sealed class ChatEmphasisTest
{
    [Test]
    [TestCase("hello world", "hello world", "plain text passthrough")]
    [TestCase("|italic text|", "[italic]italic text[/italic]", "italic delimiters")]
    [TestCase("+bold text+", "[bold]bold text[/bold]", "bold delimiters")]
    [TestCase("^small text^", "[font size=8]small text[/font]", "small delimiters")]
    [TestCase("|hello +world+|", "[italic]hello [bold]world[/bold][/italic]", "nested italic + bold")]
    [TestCase("+^small bold^+", "[bold][font size=8]small bold[/font][/bold]", "nested bold + small")]
    [TestCase("~|not italic~|", "|not italic|", "escaped italic delimiters")]
    [TestCase("~+not bold~+", "+not bold+", "escaped bold delimiters")]
    [TestCase("~^not small~^", "^not small^", "escaped small delimiters")]
    [TestCase("|hello", "|hello", "unclosed delimiter ignored")]
    [TestCase("|hello +world+", "|hello [bold]world[/bold]", "partial closing delimiters")]
    [TestCase("|a||b|", "[italic]a[/italic][italic]b[/italic]", "adjacent same delimiters")]
    [TestCase("||", "||", "empty emphasis")]
    [TestCase("no delimiters here", "no delimiters here", "no delimiters")]
    [TestCase("|a ~| b|", "[italic]a | b[/italic]", "escaped delimiter inside emphasis")]
    [TestCase("|a ~ b|", "[italic]a ~ b[/italic]", "lone tilde inside emphasis is literal")]
    [TestCase("|~~ ~~~ ~~x|", "[italic]~~ ~~~ ~~x[/italic]", "muffled whisper text inside emphasis")]
    public void ConvertCustomEmphasisDelimiters(string input, string expected, string reason)
    {
        var result = ChatSystem.ConvertCustomEmphasisToBBCode(input);
        Assert.That(result, Is.EqualTo(expected), $"Failed for {reason}: {input}");
    }

    /// <summary>
    ///     A muffled whisper replaces most characters with "~". When that also swallowed the closing delimiter, the
    ///     parser took exponential time on the run of tildes and hung the server (2026-11-11). These must stay
    ///     effectively instant, and must come back converted rather than rescued by the match timeout.
    /// </summary>
    [Test]
    public void LongTildeRunsDoNotStall()
    {
        var inputs = new[]
        {
            "on the |" + new string('~', 900),                 // opener that never closes, then tildes
            "The mat silo is on the |~~a~~~~~~~ ~~~e ~~ ~~~~~~~ ~~~~~~~~~ ~~~~, ~~~ ~~e ~~~h ~~~ ~~~~~~~",
            "|" + string.Concat(Enumerable.Repeat("~+", 450)), // escapes of another delimiter
            "+" + string.Concat(Enumerable.Repeat("~|", 450)),
            string.Concat(Enumerable.Repeat("|+^", 333)),
        };

        // Warm up: the first call pays for compiling the pattern.
        ChatSystem.ConvertCustomEmphasisToBBCode("|warm up|");

        foreach (var input in inputs)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ChatSystem.ConvertCustomEmphasisToBBCode(input);
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(50), $"Took too long on: {input[..Math.Min(40, input.Length)]}...");
        }

        // The unclosed opener stays as typed; only the escape at the very end is consumed.
        Assert.That(ChatSystem.ConvertCustomEmphasisToBBCode("|" + new string('~', 60) + " +ok+"),
            Is.EqualTo("|" + new string('~', 60) + " [bold]ok[/bold]"));
    }
}
