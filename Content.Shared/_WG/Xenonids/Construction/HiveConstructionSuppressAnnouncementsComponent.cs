using Content.Shared._WG.Xenonids.ManageHive;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Construction;

[RegisterComponent, NetworkedComponent]
[Access(typeof(ManageHiveSystem))]
public sealed partial class HiveConstructionSuppressAnnouncementsComponent : Component;
