namespace Content.Server._WG.ExtraInteractions;

/// <summary>
/// When a mob may next show an item.
/// </summary>
[RegisterComponent, Access(typeof(WGShowItemSystem))]
public sealed partial class WGShowItemCooldownComponent : Component
{
    [ViewVariables]
    public TimeSpan NextShow;
}
