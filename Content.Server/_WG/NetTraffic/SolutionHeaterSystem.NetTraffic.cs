namespace Content.Server.Chemistry.EntitySystems;

public sealed partial class SolutionHeaterSystem
{
    /// <summary>Seconds of heating applied at once. The same energy is added; only the number of updates changes.</summary>
    private const float WgHeatStep = 0.5f;

    private float _wgHeatTime;
}
