#nullable enable
using Content.Server.Chat.Systems;

namespace Content.IntegrationTests.Tests._WF.ChatFormatting;

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
    public void ConvertCustomEmphasisDelimiters(string input, string expected, string reason)
    {
        var result = ChatSystem.ConvertCustomEmphasisToBBCode(input);
        Assert.That(result, Is.EqualTo(expected), $"Failed for {reason}: {input}");
    }
}
