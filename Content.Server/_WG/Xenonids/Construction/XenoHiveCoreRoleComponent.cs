namespace Content.Server._WG.Xenonids.Construction;

[RegisterComponent]
[Access(typeof(XenoPylonSystem))]
public sealed partial class XenoHiveCoreRoleComponent : Component
{
    [DataField]
    public EntityUid? Core;
}
