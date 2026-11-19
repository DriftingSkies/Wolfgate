namespace Content.Shared.Animals;

public sealed partial class UdderSystem
{
    /// <summary>Sets when an animal's udder next fills, as when its mood hurries or holds it back.</summary>
    public void WgSetNextGrowth(UdderComponent component, TimeSpan next)
    {
        component.NextGrowth = next;
    }
}
