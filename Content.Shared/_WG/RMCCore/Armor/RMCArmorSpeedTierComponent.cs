using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Armor;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(CMArmorSystem))]
public sealed partial class RMCArmorSpeedTierComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? SpeedTier;
}
