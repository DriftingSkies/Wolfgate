using System.Diagnostics.CodeAnalysis;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Biotype;

/// <summary>Allows a loadout only for characters whose effective biotype is listed.</summary>
public sealed partial class WGBiotypeLoadoutEffect : LoadoutEffect
{
    [DataField(required: true)]
    public HashSet<WGBiotype> Biotypes = new();

    public override bool Validate(HumanoidCharacterProfile profile, RoleLoadout loadout, ICommonSession? session, IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        if (Biotypes.Contains(WGBiotypeGates.Effective(profile, collection.Resolve<IPrototypeManager>())))
        {
            reason = null;
            return true;
        }

        reason = FormattedMessage.FromUnformatted(Loc.GetString("wg-biotype-loadout-restriction"));
        return false;
    }
}
