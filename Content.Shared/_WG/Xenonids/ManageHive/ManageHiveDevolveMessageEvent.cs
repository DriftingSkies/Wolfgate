using Content.Shared._WG.RMCCore.Dialog;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.ManageHive;

[Serializable, NetSerializable]
public sealed record ManageHiveDevolveMessageEvent(EntProtoId Choice, string Message = "") : DialogInputEvent(Message);
