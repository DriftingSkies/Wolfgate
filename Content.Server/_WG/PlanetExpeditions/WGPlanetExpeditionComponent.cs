using Content.Shared.Salvage.Expeditions;

namespace Content.Server._WG.PlanetExpeditions;

/// <summary>On a station with an expedition console: which site each offered mission points at, and the mission underway.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGPlanetExpeditionComponent : Component
{
    /// <summary>The site behind each offer, by the offer's mission index.</summary>
    [ViewVariables]
    public readonly Dictionary<ushort, WGPlanetExpeditionOffer> Offers = new();

    [ViewVariables]
    public WGPlanetExpeditionActive? Active;
}

/// <summary>A site on a ground, by its index in the ground's record.</summary>
public sealed record WGPlanetExpeditionOffer(EntityUid Ground, int SiteIndex);

/// <summary>A claimed site mission: what must be cleared, and by when once the crew has reached it.</summary>
public sealed class WGPlanetExpeditionActive
{
    public EntityUid Ground;
    public int SiteIndex;
    public SalvageMissionParams Params = default!;
    public SalvageMission Mission = default!;

    /// <summary>The structures or creature the crew must destroy; the mission completes when none is left.</summary>
    public List<EntityUid> Objectives = new();

    /// <summary>The hostiles spawned with them, removed if the mission ends unfinished.</summary>
    public List<EntityUid> Hostiles = new();

    /// <summary>Whether the crew has reached the site; the clock runs from then, to <see cref="EndTime"/>.</summary>
    public bool Arrived;

    /// <summary>When the mission fails if nobody has reached the site.</summary>
    public TimeSpan ArriveBy;

    public TimeSpan EndTime;
    public bool WarnedTravel;
    public bool WarnedMinutes;
    public bool WarnedSeconds;
}
