using Content.Server.Animals.Components;

namespace Content.Server.Animals.Systems;

public sealed partial class EggLayerSystem
{
    /// <summary>Sets when an animal next lays, as when it comes back with an outpost save.</summary>
    public void WfSetNextGrowth(EggLayerComponent component, TimeSpan next)
    {
        component.NextGrowth = next;
    }
}
