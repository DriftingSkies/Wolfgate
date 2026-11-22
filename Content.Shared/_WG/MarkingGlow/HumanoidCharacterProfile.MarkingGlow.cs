using System.Linq;
using Content.Shared._WG.MarkingGlow;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>What of this character glows: marking IDs and the eye and hair keys of <see cref="MarkingGlowRules"/>.</summary>
    [DataField]
    public List<string> MarkingGlow { get; set; } = new();

    /// <summary>The profile with one marking, or the eyes or hair, made to glow or not.</summary>
    public HumanoidCharacterProfile WithMarkingGlow(string entry, bool on)
    {
        return new(this) { MarkingGlow = MarkingGlowRules.Set(MarkingGlow, entry, on) };
    }

    /// <summary>Whether a marking ID or key glows.</summary>
    public bool Glows(string entry)
    {
        return MarkingGlow.Contains(entry);
    }

    private bool MarkingGlowEqual(HumanoidCharacterProfile other)
    {
        return MarkingGlow.SequenceEqual(other.MarkingGlow);
    }

    /// <summary>Drops glow for markings the character no longer wears. Crafted network data can deliver null.</summary>
    private void EnsureMarkingGlowValid()
    {
        MarkingGlow = MarkingGlowRules.Clean(MarkingGlow, Appearance.Markings);
    }
}
