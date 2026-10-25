using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.RolledSleeves;

/// <summary>
/// A jumpsuit that can be worn with its sleeves rolled up or its top rolled down and tied at the waist. The server
/// adds it to all inner clothing at map init.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(WFRolledSleevesSystem))]
public sealed partial class WFRolledSleevesComponent : Component
{
    [DataField, AutoNetworkedField]
    public WFJumpsuitStyle Style;
}

[Serializable, NetSerializable]
public enum WFJumpsuitStyle : byte
{
    Normal,
    SleevesRolled,
    RolledDown,
}
