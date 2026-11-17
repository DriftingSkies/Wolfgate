using Content.Server._WG.WolfChem.Narcotics;

namespace Content.Server.Medical;

/// <summary>Gives a scan the Narcotics conditions readout.</summary>
public sealed partial class HealthAnalyzerSystem
{
    [Dependency] private WGAnalyzerConditionsSystem _wgConditions = default!;
}
