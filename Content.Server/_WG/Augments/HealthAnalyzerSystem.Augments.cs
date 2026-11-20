using Content.Server._WG.Augments;

namespace Content.Server.Medical;

/// <summary>Gives a scan the list of cyberware in the body.</summary>
public sealed partial class HealthAnalyzerSystem
{
    [Dependency] private WGAugmentSystem _wgAugments = default!;
}
