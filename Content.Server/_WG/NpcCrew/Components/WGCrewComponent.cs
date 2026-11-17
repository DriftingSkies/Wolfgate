using Content.Shared._WG.NpcCrew;
using Content.Server._WG.ShipAccess;
using Content.Shared._WG.ShipAccess;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>
/// An NPC crewman: works its duty at its post and fights by its engagement rule. Mirrored to the HTN blackboard
/// by <c>WGCrewSystem</c>.
/// </summary>
[RegisterComponent]
public sealed partial class WGCrewComponent : Component
{
    /// <summary>Mission flight limits retained even when the crew has no living pilot.</summary>
    [DataField]
    public WGCrewNavigationSettings Navigation = new();

    /// <summary>Limits local action chatter independently of important radio reports.</summary>
    public TimeSpan NextSpeech;
    public Dictionary<string, TimeSpan> SpokenActions = new();
    public TimeSpan NextReport;
    public TimeSpan NextPatrol;
    public Dictionary<EntityUid, TimeSpan> RadioSightings = new();

    /// <summary>When each attacker last struck this crewman himself; only a fresh blow sends him after someone unseen.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Struck = new();

    /// <summary>Whether this crew puts its troubles on the air. Raiders don't.</summary>
    [DataField]
    public bool CallsForHelp = true;

    [DataField]
    public ProtoId<WGCrewRolePrototype>? Role;

    /// <summary>The crew profile the crewman was spawned with, for the titles it gives its roles.</summary>
    [DataField]
    public ProtoId<WGCrewProfilePrototype>? Profile;

    /// <summary>Duty branch of the crew HTN root worked when not fighting.</summary>
    [DataField]
    public string Duty = WGCrewDuties.Guard;

    [DataField]
    public WGCrewEngagement Engagement = WGCrewEngagement.OnSight;

    /// <summary>Whether on-sight crew share targets with their group aboard this ship.</summary>
    [DataField]
    public bool ShareAlerts = true;

    /// <summary>Keep this crewman's AI running without nearby player bodies.</summary>
    [DataField]
    public bool KeepActive = true;

    /// <summary>Whether this crewman goes looking for hostiles it cannot see. Off, it fights only what is in sight.</summary>
    [DataField]
    public bool Pursues = true;

    /// <summary>Everyone aboard with the same group is one crew.</summary>
    [DataField]
    public string Group = string.Empty;

    /// <summary>Ships whose crews share a battlegroup are allies and answer each other's threats.</summary>
    [DataField]
    public string Battlegroup = string.Empty;

    /// <summary>How well this crewman's crew shoots, lays ship guns and flies; the same for everyone aboard.</summary>
    [DataField]
    public WGCrewSkill Skill = WGCrewSkill.Veteran;

    /// <summary>Tiers this crewman stands above or below <see cref="Skill"/>, from his profile's loadout. See <see cref="WGCrewSkills.Effective"/>.</summary>
    [DataField]
    public int SkillOffset;

    /// <summary>The crewman's gun refills itself when it runs dry, so he never falls back to his fists.</summary>
    [DataField]
    public bool InfiniteAmmo;

    /// <summary>When this crew stops attacking another ship.</summary>
    [DataField]
    public WGCrewDisengage Disengage = WGCrewDisengage.Disable;

    /// <summary>Deter: the distance beyond which an attacker is left alone.</summary>
    [DataField]
    public float DisengageRange = 500f;

    /// <summary>Where the duty is worked and where the crewman returns to. Grid-relative.</summary>
    [DataField]
    public EntityCoordinates? Post;

    /// <summary>How close to the post counts as being at it.</summary>
    [DataField]
    public float PostRange = 1.5f;

    /// <summary>The ship whose allow list holds this crewman's card, so deleting him revokes it.</summary>
    [ViewVariables]
    public EntityUid? AccessShip;

    /// <summary>The card key enrolled on <see cref="AccessShip"/>, kept so it is revoked even once the card is taken.</summary>
    [ViewVariables]
    public WGShipAccessKey? AccessKey;

    /// <summary><see cref="AccessKey"/>'s entry and door lists while he is dead, put back if he is revived.</summary>
    [ViewVariables]
    public WGShipAccessHold? AccessHold;

    /// <summary>Whether the role title has been put in front of the name.</summary>
    [ViewVariables]
    public bool Titled;
}
