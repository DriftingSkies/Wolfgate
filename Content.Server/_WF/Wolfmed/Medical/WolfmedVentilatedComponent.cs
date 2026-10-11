namespace Content.Server._WF.Wolfmed.Medical;

/// <summary>
/// A ventilator is breathing for this body. Each breath with gas in it pushes <see cref="Until"/> on, so a stand that
/// runs dry, breaks or is taken away stops counting within a breath.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedVentilatedComponent : Component
{
    [ViewVariables]
    public TimeSpan Until;
}
