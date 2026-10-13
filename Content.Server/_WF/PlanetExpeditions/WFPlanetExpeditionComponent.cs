using Content.Shared.Salvage.Expeditions;

namespace Content.Server._WF.PlanetExpeditions;

/// <summary>On a station with an expedition console: which site each offered mission points at, and the mission underway.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetExpeditionComponent : Component
{
    /// <summary>The site behind each offer, by the offer's mission index.</summary>
    [ViewVariables]
    public readonly Dictionary<ushort, WFPlanetExpeditionOffer> Offers = new();

    [ViewVariables]
    public WFPlanetExpeditionActive? Active;
}

/// <summary>A site on a ground, by its index in the ground's record.</summary>
public sealed record WFPlanetExpeditionOffer(EntityUid Ground, int SiteIndex);

/// <summary>A claimed site mission: what must be cleared and by when.</summary>
public sealed class WFPlanetExpeditionActive
{
    public EntityUid Ground;
    public int SiteIndex;
    public SalvageMissionParams Params = default!;
    public SalvageMission Mission = default!;

    /// <summary>The structures or creature the crew must destroy; the mission completes when none is left.</summary>
    public List<EntityUid> Objectives = new();

    public TimeSpan EndTime;
    public bool WarnedMinutes;
    public bool WarnedSeconds;
}
