using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Armor;

[RegisterComponent, NetworkedComponent]
[Access(typeof(CMArmorSystem))]
public sealed partial class ClothingIgnoreBlockBackpackComponent : Component;
