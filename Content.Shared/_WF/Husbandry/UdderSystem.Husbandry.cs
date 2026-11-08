namespace Content.Shared.Animals;

public sealed partial class UdderSystem
{
    /// <summary>Sets when an animal's udder next fills, as when it comes back with an outpost save.</summary>
    public void WfSetNextGrowth(UdderComponent component, TimeSpan next)
    {
        component.NextGrowth = next;
    }
}
