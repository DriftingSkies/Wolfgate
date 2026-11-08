namespace Content.Server._WF.OutpostRaids;

/// <summary>An early warning mast: while powered on an outpost, it sounds the outpost's alarm this long before a raid lands.</summary>
[RegisterComponent, Access(typeof(WFRaidSystem))]
public sealed partial class WFEarlyWarningComponent : Component
{
    [DataField]
    public TimeSpan Lead = TimeSpan.FromSeconds(120);
}
