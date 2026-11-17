using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.RolledSleeves;

/// <summary>
/// A jumpsuit that can be worn with its sleeves rolled up or its top rolled down and tied at the waist. The server
/// adds it to all inner clothing at map init.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(WGRolledSleevesSystem))]
public sealed partial class WGRolledSleevesComponent : Component
{
    [DataField, AutoNetworkedField]
    public WGJumpsuitStyle Style;
}

[Serializable, NetSerializable]
public enum WGJumpsuitStyle : byte
{
    Normal,
    SleevesRolled,
    RolledDown,
}
