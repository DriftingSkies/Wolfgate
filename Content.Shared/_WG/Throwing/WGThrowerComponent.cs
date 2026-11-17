namespace Content.Shared._WG.Throwing;

/// <summary>Items this entity threw that are still in flight, so their thrower is cleared if it is deleted first.</summary>
[RegisterComponent]
public sealed partial class WGThrowerComponent : Component
{
    [ViewVariables]
    public HashSet<EntityUid> InFlight = new();
}
