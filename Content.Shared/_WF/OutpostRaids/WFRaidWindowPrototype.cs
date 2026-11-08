using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.OutpostRaids;

/// <summary>
/// When outposts on a world can be raided: the Active window, from dusk to dawn by default, and while a storm rages.
/// One with no surfaces is the default for every world without its own.
/// </summary>
[Prototype("wfRaidWindow")]
public sealed partial class WFRaidWindowPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The worlds this window is for.</summary>
    [DataField]
    public List<ProtoId<WFPlanetSurfacePrototype>> Surfaces = new();

    /// <summary>The local minute the window opens.</summary>
    [DataField]
    public int Opens = 18 * 60;

    /// <summary>The local minute it closes.</summary>
    [DataField]
    public int Closes = 6 * 60;

    /// <summary>Whether a raging storm opens it too, outside its hours.</summary>
    [DataField]
    public bool Storms = true;

    /// <summary>A world whose outposts are never raided.</summary>
    [DataField]
    public bool Never;
}
