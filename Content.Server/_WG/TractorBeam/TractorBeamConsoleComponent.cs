namespace Content.Server._WG.TractorBeam;

[RegisterComponent]
public sealed partial class TractorBeamConsoleComponent : Component
{
    [DataField] public float Range = 240f;
    public TimeSpan NextCommand;
}
