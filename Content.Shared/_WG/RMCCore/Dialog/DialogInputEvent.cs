using Robust.Shared.Serialization;

namespace Content.Shared._WG.RMCCore.Dialog;

[Serializable, NetSerializable]
public abstract record DialogInputEvent(string Message);
