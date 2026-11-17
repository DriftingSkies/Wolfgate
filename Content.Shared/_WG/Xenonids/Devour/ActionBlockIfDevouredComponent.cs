using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Devour;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoDevourSystem))]
public sealed partial class ActionBlockIfDevouredComponent : Component;
