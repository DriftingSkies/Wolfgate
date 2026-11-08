namespace Content.Shared.Animals;

public sealed partial class WoolySystem
{
    /// <summary>Sets when an animal's wool next grows, as when it comes back with an outpost save.</summary>
    public void WfSetNextGrowth(WoolyComponent component, TimeSpan next)
    {
        component.NextGrowth = next;
    }
}
