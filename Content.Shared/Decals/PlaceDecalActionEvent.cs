using Content.Shared.Actions;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared.Decals;

public sealed partial class PlaceDecalActionEvent : WorldTargetActionEvent
{
    [DataField("decalId", customTypeSerializer:typeof(PrototypeIdSerializer<DecalPrototype>), required:true)]
    public string DecalId = string.Empty;

    [DataField("color")]
    public Color Color;

    [DataField("rotation")]
    public double Rotation;

    [DataField("snap")]
    public bool Snap;

    [DataField("zIndex")]
    public int ZIndex;

    [DataField("cleanable")]
    public bool Cleanable;
}
