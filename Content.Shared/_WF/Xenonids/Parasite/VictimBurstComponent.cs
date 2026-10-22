using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Xenonids.Parasite;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedXenoParasiteSystem))]
public sealed partial class VictimBurstComponent : Component
{
    [DataField, AutoNetworkedField]
    public ResPath RsiPath = new("/Textures/_WF/Xenonids/Effects/burst.rsi");

    [DataField, AutoNetworkedField]
    public string BurstState = "bursted_stand";

    [DataField, AutoNetworkedField]
    public string BurstingState = "burst_stand";

    /// <summary>WOLFGATE: the player who was the host when it burst.</summary>
    [DataField, AutoNetworkedField]
    public NetUserId? HostUserId;

    /// <summary>WOLFGATE: the mind that was in the host when it burst, which may have ghosted since.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? HostMind;

    /// <summary>WOLFGATE: that player took the larva, so the body can no longer be mended.</summary>
    [DataField, AutoNetworkedField]
    public bool HostLeft;

    /// <summary>WOLFGATE: a surgeon has cleared the burst chest; the mend step can follow.</summary>
    [DataField, AutoNetworkedField]
    public bool Cleared;
}

[Serializable, NetSerializable]
public enum VictimBurstState : byte
{
    Bursting = 1,
    Burst = 2
}
