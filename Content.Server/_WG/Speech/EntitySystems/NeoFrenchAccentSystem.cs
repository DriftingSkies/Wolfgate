using Content.Server._WG.Speech.Components;
using Content.Server.Speech;
using Content.Server.Speech.EntitySystems;
using Robust.Shared.Random;
using System.Text.RegularExpressions;

namespace Content.Server._WG.Speech.EntitySystems;

/// <summary>
/// System that gives the speaker an alternate faux-French accent.
/// </summary>
public sealed partial class NeoFrenchAccentSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ReplacementAccentSystem _replacement = default!;

    private static readonly Regex RegexTh = new(@"th", RegexOptions.IgnoreCase);
    private static readonly Regex RegexSpacePunctuation = new(@"(?<=\w\w)[!?;:](?!\w)", RegexOptions.IgnoreCase);
    private static readonly Regex RegexI = new(@"(?<!\w)i(?!\w)|i", RegexOptions.IgnoreCase);
    private static readonly Regex RegexFinalEr = new(@"er\b", RegexOptions.IgnoreCase);
    private static readonly Regex RegexR = new(@"r", RegexOptions.IgnoreCase);
    private static readonly Regex RegexInitialVowel = new(@"\b([aeiou])", RegexOptions.IgnoreCase);
    private static readonly Regex RegexCh = new(@"ch", RegexOptions.IgnoreCase);
    private static readonly Regex RegexH = new(@"(?<![cs])h", RegexOptions.IgnoreCase);
    private static readonly Regex RegexSEdge = new(@"(?<=[aeiou])s\b|\bs(?=[aeiou])", RegexOptions.IgnoreCase);
    private static readonly Regex RegexSBetweenVowels = new(@"(?<=[aeiou])s(?=[aeiou])", RegexOptions.IgnoreCase);
    private static readonly Regex RegexS = new(@"s", RegexOptions.IgnoreCase);
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NeoFrenchAccentComponent, AccentGetEvent>(OnAccentGet);
    }

    public string Accentuate(string message, NeoFrenchAccentComponent component)
    {
        var msg = message;

        msg = _replacement.ApplyReplacements(msg, "WGNeoFrench");

        // Replaces "th" with "'z" (65%) or "'d" (35%).
        msg = RegexTh.Replace(msg, match =>
        {
            return _random.Prob(0.35f) ? "'d" : "'z";
        });

        // Converts "ch" to "sh"
        msg = RegexCh.Replace(msg, "sh");

        // Replaces "h" with "'" unless it is part of "sh".
        msg = RegexH.Replace(msg, "'");

        // Replaces "s" with "z" at word edges when adjacent to a vowel.
        msg = RegexSEdge.Replace(msg, "z");

        // Replaces "s" with "z" between vowels.
        msg = RegexSBetweenVowels.Replace(msg, "z");

        // Replaces other "s" with "z" at a low chance.
        msg = RegexS.Replace(msg, match =>
        {
            return _random.Prob(0.15f) ? "z" : match.Value;
        });

        // Spaces out ! ? : and ;.
        msg = RegexSpacePunctuation.Replace(msg, " $&");

        msg = RegexI.Replace(msg, match =>
        {
            return match.Value.Equals("I", StringComparison.Ordinal) &&
                match.Index + match.Length < msg.Length &&
                char.IsWhiteSpace(msg[match.Index + match.Length])
                ? "I"
                : "e";
        });

        // Replaces final "er" with "eur".
        msg = RegexFinalEr.Replace(msg, "eur");

        // Randomly doubles "r" (50%), excluding the r in "eur".
        msg = RegexR.Replace(msg, match =>
        {
            if (match.Index >= 2 &&
                msg.Substring(match.Index - 2, 2).Equals("eu", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            return _random.Prob(0.5f) ? "rr" : "r";
        });

        // Randomly adds an initial "h" before vowels (20%).
        // Does not affect words beginning with "y".
        msg = RegexInitialVowel.Replace(msg, match =>
        {
            return _random.Prob(0.2f)
                ? "h" + match.Value
                : match.Value;
        });

        return msg;
    }

    private void OnAccentGet(EntityUid uid, NeoFrenchAccentComponent component, AccentGetEvent args)
    {
        args.Message = Accentuate(args.Message, component);
    }
}
