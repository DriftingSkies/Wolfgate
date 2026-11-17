using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.ManageHive.Boons;

[RegisterComponent, NetworkedComponent]
[Access(typeof(HiveBoonSystem))]
public sealed partial class HiveClusterComponent : Component;
