namespace Content.Server._WG.Remade.Roster;

/// <summary>A Remade that dies when every one of these limbs is gone. The lurker has it for its tentacles.</summary>
[RegisterComponent]
public sealed partial class WGDiesWithoutLimbsComponent : Component
{
    [DataField(required: true)]
    public List<string> Limbs = new();
}
