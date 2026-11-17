using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.ManageHive;

[Serializable, NetSerializable]
public record ManageHiveDevolveConfirmEvent(EntProtoId Choice);
