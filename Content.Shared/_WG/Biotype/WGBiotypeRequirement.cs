using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Biotype;

/// <summary>Requires the character's effective biotype to be, or with <c>inverted</c> not to be, one of those listed.</summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class WGBiotypeRequirement : JobRequirement
{
    [DataField(required: true)]
    public HashSet<WGBiotype> Biotypes = new();

    public override bool Check(IEntityManager entManager,
        IPrototypeManager protoManager,
        HumanoidCharacterProfile? profile,
        IReadOnlyDictionary<string, TimeSpan> playTimes,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        var names = string.Join(" ", Biotypes.Select(b => Loc.GetString(WGBiotypeGates.NameKey(b))));
        var key = Inverted ? "wg-biotype-requirement-blacklisted" : "wg-biotype-requirement-whitelisted";
        reason = FormattedMessage.FromMarkupPermissive($"{Loc.GetString(key)}\n[color=yellow]{names}[/color]");

        // A null profile is a ghost role; don't block it.
        if (profile is null)
            return true;

        return Biotypes.Contains(WGBiotypeGates.Effective(profile, protoManager)) != Inverted;
    }
}
