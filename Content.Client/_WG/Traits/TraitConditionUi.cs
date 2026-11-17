using Content.Shared._WG.Traits;
using Content.Shared._WG.Traits.Conditions;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Traits;

/// <summary>
/// Condition checks and tooltip lines for the character editor.
/// </summary>
public static class TraitConditionUi
{
    /// <summary>
    /// Checks the conditions the editor can know from the profile. Job conditions stay open here because the
    /// character has not been assigned a job yet; the server checks them when the character spawns.
    /// </summary>
    public static bool AllMet(TraitPrototype trait, string speciesId, IPrototypeManager proto)
    {
        if (trait.Conditions.Count == 0)
            return true;

        var ctx = new TraitConditionContext
        {
            SpeciesId = speciesId,
            Proto = proto,
        };

        return TraitConditionEvaluator.AllMet(trait.Conditions, ctx);
    }

    /// <summary>
    /// Appends a tooltip line for each condition that describes a requirement.
    /// </summary>
    public static void AddTooltipLines(TraitPrototype trait, IPrototypeManager proto, List<string> lines,
        bool onlyUnmet = false, string? speciesId = null)
    {
        var loc = IoCManager.Resolve<ILocalizationManager>();
        var ctx = speciesId == null
            ? null
            : new TraitConditionContext { SpeciesId = speciesId, Proto = proto };

        foreach (var condition in trait.Conditions)
        {
            if (onlyUnmet && ctx != null && condition.Evaluate(ctx))
                continue;

            var line = condition.GetTooltip(proto, loc);
            if (!string.IsNullOrEmpty(line))
                lines.Add(line);
        }
    }
}
