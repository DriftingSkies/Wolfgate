using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>A crewman who now and then goes after vermin loose on his ship, such as the janitor after mice.</summary>
[RegisterComponent]
public sealed partial class WGCrewPestHunterComponent : Component
{
    /// <summary>The animals he hunts.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Pests = new();

    /// <summary>How often he looks round for one.</summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromMinutes(2);

    /// <summary>The chance, each look, that he bothers.</summary>
    [DataField]
    public float Chance = 0.5f;

    /// <summary>How far from him, in metres, a pest may be.</summary>
    [DataField]
    public float Range = 15f;

    /// <summary>How long he chases one before giving up.</summary>
    [DataField]
    public TimeSpan HuntTime = TimeSpan.FromSeconds(45);

    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public TimeSpan Until;

    [ViewVariables]
    public TimeSpan NextLook;
}
