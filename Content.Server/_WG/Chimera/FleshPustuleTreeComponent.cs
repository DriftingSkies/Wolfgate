namespace Content.Server._WG.Chimera;

/// <summary>Each tree yields once; the harvested state is preserved across saves.</summary>
[RegisterComponent]
public sealed partial class WGFleshPustuleTreeComponent : Component
{
    [DataField] public bool Harvested;
}

/// <summary>A harvested pustule that can be planted or thrown to burst.</summary>
[RegisterComponent]
public sealed partial class WGFleshPustuleItemComponent : Component
{
    [ViewVariables] public bool Thrown;
    [ViewVariables] public bool Consumed;
}
