using System.Linq;
using System.Text;
using Content.Shared._WG.Lattice;
using Robust.Shared.Utility;

namespace Content.Server._WG.Fragments;

/// <summary>
/// Builds the fragment's corrupted chat text: Lattice noise, hidden words written over real ones and bursts that
/// flicker live through the client's scramble tag. A span the caller keeps, such as the coordinates, is never touched.
/// </summary>
public static class WGFragmentText
{
    /// <summary>The noise the flickering bursts draw from, the same set the Lattice garbles with.</summary>
    public const string Noise = "#$%@!&*?~^";

    /// <summary>A line as chat markup, and as plain text for the logs.</summary>
    public readonly record struct Built(string Markup, string Plain);

    private enum Kind : byte
    {
        Text,
        Keep,
        Hidden,
        Flicker,
    }

    /// <summary>Corrupts <paramref name="text"/>, leaving the first <paramref name="keep"/> in it as written.</summary>
    public static Built Build(string text, string? keep, float corruption, int hidden, int flickers,
        IReadOnlyList<string> words, int flickerRate, System.Random random)
    {
        var chars = new List<(char C, Kind K)>(text.Length);
        var at = string.IsNullOrEmpty(keep) ? -1 : text.IndexOf(keep, StringComparison.Ordinal);
        if (at < 0)
        {
            Add(chars, WGLatticeMath.Garble(text, corruption, random.Next()), Kind.Text);
        }
        else
        {
            Add(chars, WGLatticeMath.Garble(text[..at], corruption, random.Next()), Kind.Text);
            Add(chars, keep!, Kind.Keep);
            Add(chars, WGLatticeMath.Garble(text[(at + keep!.Length)..], corruption, random.Next()), Kind.Text);
        }

        // Whole words that may be written over; each takes at most one hidden word or burst.
        var spans = new List<(int Start, int End)>();
        for (var i = 0; i < chars.Count;)
        {
            if (chars[i].K != Kind.Text || char.IsWhiteSpace(chars[i].C))
            {
                i++;
                continue;
            }

            var j = i;
            while (j < chars.Count && chars[j].K == Kind.Text && !char.IsWhiteSpace(chars[j].C))
                j++;
            spans.Add((i, j));
            i = j;
        }

        var used = new HashSet<int>();
        (int Start, int End)? Pick(int minLength)
        {
            var open = new List<int>();
            for (var i = 0; i < spans.Count; i++)
            {
                if (!used.Contains(i) && spans[i].End - spans[i].Start >= minLength)
                    open.Add(i);
            }

            if (open.Count == 0)
                return null;
            var pick = open[random.Next(open.Count)];
            used.Add(pick);
            return spans[pick];
        }

        for (var h = 0; h < hidden && words.Count > 0; h++)
        {
            var word = words[random.Next(words.Count)];
            if (Pick(word.Length) is not { } span)
                continue;
            for (var k = 0; k < word.Length; k++)
                chars[span.Start + k] = (word[k], Kind.Hidden);
        }

        for (var f = 0; f < flickers; f++)
        {
            if (Pick(3) is not { } span)
                continue;
            var length = Math.Min(span.End - span.Start, 3 + random.Next(2));
            var start = span.Start + random.Next(span.End - span.Start - length + 1);
            for (var k = 0; k < length; k++)
                chars[start + k] = (chars[start + k].C, Kind.Flicker);
        }

        var markup = new StringBuilder();
        var plain = new StringBuilder();
        for (var i = 0; i < chars.Count;)
        {
            var j = i;
            while (j < chars.Count && chars[j].K == chars[i].K)
                j++;

            if (chars[i].K == Kind.Flicker)
            {
                markup.Append(Scramble(j - i, flickerRate));
                for (var k = i; k < j; k++)
                    plain.Append(Noise[random.Next(Noise.Length)]);
            }
            else
            {
                var run = new StringBuilder(j - i);
                for (var k = i; k < j; k++)
                    run.Append(chars[k].C);
                markup.Append(FormattedMessage.EscapeText(run.ToString()));
                plain.Append(run);
            }

            i = j;
        }

        return new Built(markup.ToString(), plain.ToString());
    }

    /// <summary>A whisper: spaced lowercase letters with one flickering burst between its middle words.</summary>
    public static Built Whisper(string text, int flickerRate)
    {
        var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var spaced = words.Select(w => string.Join(' ', w.ToCharArray())).ToList();
        var cut = Math.Max(1, spaced.Count / 2);

        var markup = FormattedMessage.EscapeText(string.Join("   ", spaced.Take(cut)))
                     + "   " + Scramble(2, flickerRate) + "   "
                     + FormattedMessage.EscapeText(string.Join("   ", spaced.Skip(cut)));
        return new Built(markup.TrimEnd(), text);
    }

    /// <summary>The client's scramble tag in a monospace font, so the burst keeps its width as it changes.</summary>
    public static string Scramble(int length, int rate)
    {
        return $"[font=\"Monospace\"][scramble rate={rate} length={length} chars=\"{Noise}\"/][/font]";
    }

    private static void Add(List<(char C, Kind K)> chars, string text, Kind kind)
    {
        foreach (var c in text)
            chars.Add((c, kind));
    }
}
