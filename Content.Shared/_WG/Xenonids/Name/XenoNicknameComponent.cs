using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Xenonids.Name;

/// <summary>Lets a xeno pick the short tag in its name once, replacing the default XX.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoNicknameComponent : Component
{
    [DataField]
    public EntProtoId Action = "WGActionXenoNickname";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public int MaxLength = 4;
}
