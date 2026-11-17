using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Audio;

namespace Content.Shared._WG.Xenonids.Respawn;

[RegisterComponent, NetworkedComponent]
public sealed partial class XenoRespawnComponent : Component
{
    [DataField]
    public EntityUid? Hive;

    [DataField]
    public TimeSpan RespawnAt;

    [DataField]
    public bool RespawnAtLocation = false;

    [DataField]
    public EntityCoordinates? Location;

    [DataField]
    public EntProtoId Larva = "CMXenoLarva";

    [DataField]
    public SoundSpecifier CorpseSound = new SoundPathSpecifier("/Audio/_WG/Xenonids/Xeno/xeno_newlarva.ogg");
}
