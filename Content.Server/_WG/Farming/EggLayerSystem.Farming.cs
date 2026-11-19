using Content.Server.Animals.Components;

namespace Content.Server.Animals.Systems;

public sealed partial class EggLayerSystem
{
    /// <summary>Sets when an animal next lays, as when its mood hurries or holds it back.</summary>
    public void WgSetNextGrowth(EggLayerComponent component, TimeSpan next)
    {
        component.NextGrowth = next;
    }
}
