using Content.Shared.Dataset;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.NpcCrew;

/// <summary>
/// What kind of people crew a ship: the bodies they come in, what each role wears and carries, how skilled they
/// are and who among them fights. Every crewman rolls his own body and loadout from the pools.
/// </summary>
[Prototype("wfCrewProfile")]
public sealed partial class WFCrewProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Crew body prototypes, one picked per crewman. List one more often to make it more common.</summary>
    [DataField]
    public List<EntProtoId> Bodies = new();

    /// <summary>Skill levels, one picked for the whole crew.</summary>
    [DataField]
    public List<WFCrewSkill> Skills = new();

    /// <summary>Loadouts by role, one picked per crewman of that role.</summary>
    [DataField]
    public Dictionary<ProtoId<WFCrewRolePrototype>, List<ProtoId<StartingGearPrototype>>> Loadouts = new();

    /// <summary>Roles that fight differently from their role's default.</summary>
    [DataField]
    public Dictionary<ProtoId<WFCrewRolePrototype>, WFCrewEngagement> Engagement = new();

    /// <summary>Chance, from 0 to 1 by role, that a crewman is a coward: he never fights and takes shelter instead.</summary>
    [DataField]
    public Dictionary<ProtoId<WFCrewRolePrototype>, float> Cowards = new();

    /// <summary>Datasets every crewman's first and last name are drawn from. Left out, each keeps the name his body rolled.</summary>
    [DataField]
    public ProtoId<DatasetPrototype>? FirstNames;

    [DataField]
    public ProtoId<DatasetPrototype>? LastNames;

    /// <summary>Components every crewman gets, such as an accent.</summary>
    [DataField]
    public ComponentRegistry Components = new();
}
