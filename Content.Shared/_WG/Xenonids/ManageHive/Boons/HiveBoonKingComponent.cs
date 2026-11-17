using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.ManageHive.Boons;

[RegisterComponent, NetworkedComponent]
[Access(typeof(ManageHiveSystem))]
public sealed partial class HiveBoonKingComponent : Component;
