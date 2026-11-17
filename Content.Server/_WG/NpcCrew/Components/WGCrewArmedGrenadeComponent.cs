using Content.Server._WG.NpcCrew.Systems;
using Robust.Shared.Map;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>On a crewman holding an armed grenade: where it goes and when it leaves his hand. See <c>WGCrewGrenadeSystem</c>.</summary>
[RegisterComponent]
public sealed partial class WGCrewArmedGrenadeComponent : Component
{
    [ViewVariables]
    public EntityUid Grenade;

    /// <summary>The spot it is thrown to.</summary>
    [ViewVariables]
    public EntityCoordinates At;

    /// <summary>The hostile it is meant for, to pick a new spot if the first stops being safe.</summary>
    [ViewVariables]
    public EntityUid Target;

    /// <summary>The rules it was planned under.</summary>
    [ViewVariables]
    public WGCrewGrenadeRules Rules;

    /// <summary>When he lets go of it, once he has held it for his cook time.</summary>
    [ViewVariables]
    public TimeSpan ThrowTime;
}
