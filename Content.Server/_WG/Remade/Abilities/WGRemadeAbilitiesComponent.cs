namespace Content.Server._WG.Remade.Abilities;

/// <summary>The abilities a Remade uses by itself while it has a target. They're tried in the order listed.</summary>
[RegisterComponent]
public sealed partial class WGRemadeAbilitiesComponent : Component
{
    [DataField(required: true)]
    public List<WGAbility> Abilities = new();

    /// <summary>The ability that's running.</summary>
    [ViewVariables]
    public WGAbility? Active;

    /// <summary>The target it had last tick, to tell when it first sees a new one.</summary>
    [ViewVariables]
    public EntityUid? LastTarget;
}
