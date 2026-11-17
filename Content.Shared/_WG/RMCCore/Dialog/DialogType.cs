using Robust.Shared.Serialization;

namespace Content.Shared._WG.RMCCore.Dialog;

[Serializable, NetSerializable]
public enum DialogType
{
    Options,
    Input,
    Confirm,
}
