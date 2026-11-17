using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Biotype;

/// <summary>Gates that follow a character's effective biotype instead of its species id.</summary>
public static class WGBiotypeGates
{
    /// <summary>The profile's biotype with Default turned into its species' own.</summary>
    public static WGBiotype Effective(HumanoidCharacterProfile profile, IPrototypeManager protoManager)
    {
        return protoManager.TryIndex(profile.Species, out var species)
            ? WGBiotypeRules.Resolve(profile.Biotype, species)
            : profile.Biotype;
    }

    /// <summary>The Fluent key of a biotype's display name.</summary>
    public static string NameKey(WGBiotype biotype)
    {
        return biotype switch
        {
            WGBiotype.Organic => "wg-biotype-organic",
            WGBiotype.Synthetic => "wg-biotype-synthetic",
            _ => "wg-biotype-default",
        };
    }

    /// <summary>Whether the trait lists the profile's effective biotype in its biotype blacklist.</summary>
    public static bool TraitBlocked(TraitPrototype trait, HumanoidCharacterProfile profile, IPrototypeManager protoManager)
    {
        return trait.BiotypeBlacklist.Count > 0 && trait.BiotypeBlacklist.Contains(Effective(profile, protoManager));
    }
}
