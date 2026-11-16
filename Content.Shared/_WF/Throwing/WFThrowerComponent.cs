namespace Content.Shared._WF.Throwing;

/// <summary>Items this entity threw that are still in flight, so their thrower is cleared if it is deleted first.</summary>
[RegisterComponent]
public sealed partial class WFThrowerComponent : Component
{
    [ViewVariables]
    public HashSet<EntityUid> InFlight = new();
}
