using Content.Shared.Chemistry.Components;

namespace Content.Server._WG.WildSlimes;

/// <summary>Spills <see cref="Solution"/> where the mob dies, once.</summary>
[RegisterComponent]
public sealed partial class WGSpillOnDeathComponent : Component
{
    [DataField(required: true)]
    public Solution Solution = new();

    [ViewVariables]
    public bool Spilled;
}
