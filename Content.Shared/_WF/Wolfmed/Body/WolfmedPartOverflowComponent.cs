using Content.Shared.Damage;

namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>
/// Damage a part took past its cap since it last had room, by type. The sever thresholds read it beside the stored
/// damage, so a capped limb comes off at the same totals it did before it had a cap.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedPartOverflowComponent : Component
{
    [ViewVariables]
    public DamageSpecifier Damage = new();
}
