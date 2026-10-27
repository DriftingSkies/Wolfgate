namespace Content.Server._WF.ExtraInteractions;

/// <summary>
/// When a mob may next show an item.
/// </summary>
[RegisterComponent, Access(typeof(WFShowItemSystem))]
public sealed partial class WFShowItemCooldownComponent : Component
{
    [ViewVariables]
    public TimeSpan NextShow;
}
