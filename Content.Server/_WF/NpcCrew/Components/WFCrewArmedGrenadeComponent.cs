using Content.Server._WF.NpcCrew.Systems;
using Robust.Shared.Map;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>On a crewman holding an armed grenade: where it goes and when it leaves his hand. See <c>WFCrewGrenadeSystem</c>.</summary>
[RegisterComponent]
public sealed partial class WFCrewArmedGrenadeComponent : Component
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
    public WFCrewGrenadeRules Rules;

    /// <summary>When he lets go of it, once he has held it for his cook time.</summary>
    [ViewVariables]
    public TimeSpan ThrowTime;
}
