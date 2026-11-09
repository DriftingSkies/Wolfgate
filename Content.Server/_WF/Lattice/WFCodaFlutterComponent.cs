namespace Content.Server._WF.Lattice;

/// <summary>An APC whose output the Coda cuts in and out until <see cref="Until"/>.</summary>
[RegisterComponent]
public sealed partial class WFCodaFlutterComponent : Component
{
    [DataField]
    public TimeSpan Until;

    [DataField]
    public TimeSpan Next;

    /// <summary>Whether the output is cut right now.</summary>
    [DataField]
    public bool Out;
}
