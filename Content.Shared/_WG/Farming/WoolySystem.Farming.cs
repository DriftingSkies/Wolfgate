namespace Content.Shared.Animals;

public sealed partial class WoolySystem
{
    /// <summary>Sets when an animal's wool next grows, as when its mood hurries or holds it back.</summary>
    public void WgSetNextGrowth(WoolyComponent component, TimeSpan next)
    {
        component.NextGrowth = next;
    }
}
