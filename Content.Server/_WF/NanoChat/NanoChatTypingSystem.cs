using System.Text;
using Content.Server._WF.WolfChem.Narcotics;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Drunk;
using Content.Shared.StatusEffect;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.NanoChat;

/// <summary>
/// What an intoxicated thumb types. Drink and heavy highs bring typos (a neighbouring key, a doubled, dropped or swapped
/// letter); a heavy stimulant high rushes the text and a heavy opioid high trails it off, as in speech. Species and
/// other speech accents don't apply to typing.
/// </summary>
public sealed class NanoChatTypingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private WFDrugSpeechSystem _drugSpeech = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;

    /// <summary>Typo chance per letter at the deepest drunkenness.</summary>
    private const float MaxDrunkRate = 0.12f;

    /// <summary>Typo chance per letter for each family's heavy and overdose phases.</summary>
    private static readonly Dictionary<WFDrugFamily, (float Heavy, float Overdose)> HighRates = new()
    {
        [WFDrugFamily.Stimulant] = (0.02f, 0.05f),
        [WFDrugFamily.Opioid] = (0.02f, 0.06f),
        [WFDrugFamily.Psychedelic] = (0.04f, 0.09f),
        [WFDrugFamily.Cannabinoid] = (0.02f, 0.04f),
    };

    private static readonly Dictionary<char, string> Neighbours = new()
    {
        ['q'] = "wa", ['w'] = "qeas", ['e'] = "wrsd", ['r'] = "etdf", ['t'] = "ryfg", ['y'] = "tugh", ['u'] = "yihj",
        ['i'] = "uojk", ['o'] = "ipkl", ['p'] = "ol", ['a'] = "qwsz", ['s'] = "awedxz", ['d'] = "serfcx",
        ['f'] = "drtgvc", ['g'] = "ftyhbv", ['h'] = "gyujnb", ['j'] = "huikmn", ['k'] = "jiolm", ['l'] = "kop",
        ['z'] = "asx", ['x'] = "zsdc", ['c'] = "xdfv", ['v'] = "cfgb", ['b'] = "vghn", ['n'] = "bhjm", ['m'] = "njk",
    };

    /// <summary>The text as the typist manages to type it.</summary>
    public string Impair(EntityUid typist, string text)
    {
        if (TryComp<WFIntoxicationComponent>(typist, out var high))
        {
            var stimulant = _intoxication.FamilyPhase(high, WFDrugFamily.Stimulant);
            if (stimulant >= WFDrugPhase.Heavy)
                text = _drugSpeech.Rush(text, stimulant == WFDrugPhase.Overdose);

            var opioid = _intoxication.FamilyPhase(high, WFDrugFamily.Opioid);
            if (opioid >= WFDrugPhase.Heavy)
                text = _drugSpeech.Trail(text, opioid == WFDrugPhase.Overdose);
        }

        var rate = TypoRate(typist);
        return rate > 0 ? Typos(text, rate) : text;
    }

    /// <summary>The chance per letter of a typo: the worse of drink and the strongest high.</summary>
    public float TypoRate(EntityUid typist)
    {
        var rate = 0f;
        if (_status.TryGetTime(typist, SharedDrunkSystem.DrunkKey, out var time))
        {
            // Same scale as slurred speech: the time left on the drunk effect.
            var left = (float) (time.Value.Item2 - _timing.CurTime).TotalSeconds;
            rate = MaxDrunkRate * Math.Clamp((left - 80) / 1100, 0f, 1f);
        }

        if (TryComp<WFIntoxicationComponent>(typist, out var high))
        {
            foreach (var (family, (heavy, overdose)) in HighRates)
            {
                rate = Math.Max(rate, _intoxication.FamilyPhase(high, family) switch
                {
                    WFDrugPhase.Heavy => heavy,
                    WFDrugPhase.Overdose => overdose,
                    _ => 0f,
                });
            }
        }

        return rate;
    }

    /// <summary>Puts a typo on each letter with the given chance.</summary>
    public string Typos(string text, float rate)
    {
        var result = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!char.IsLetter(c) || !_random.Prob(rate))
            {
                result.Append(c);
                continue;
            }

            var roll = _random.NextFloat();
            if (roll < 0.45f && Neighbours.TryGetValue(char.ToLowerInvariant(c), out var keys))
            {
                var slip = _random.Pick(keys.ToCharArray());
                result.Append(char.IsUpper(c) ? char.ToUpperInvariant(slip) : slip);
            }
            else if (roll < 0.65f)
            {
                result.Append(c).Append(c);
            }
            else if (roll < 0.85f || i + 1 >= text.Length || !char.IsLetter(text[i + 1]))
            {
                // Dropped.
            }
            else
            {
                result.Append(text[i + 1]).Append(c);
                i++;
            }
        }

        return result.ToString();
    }
}
