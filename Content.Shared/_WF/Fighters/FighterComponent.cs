using Content.Shared.Atmos;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Fighters;

/// <summary>
/// A small craft one pilot climbs into and flies directly: thrust and strafe on the shuttle keys, turning on the
/// rotate keys or towards the cursor, and a nose gun that is the fighter itself.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FighterComponent : Component
{
    public const string PilotSlotId = "fighter-pilot-slot";

    [ViewVariables]
    public ContainerSlot PilotSlot = default!;

    [DataField, AutoNetworkedField]
    public EntityUid? Pilot;

    [DataField]
    public EntityWhitelist? PilotWhitelist;

    [DataField]
    public TimeSpan EntryDelay = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan ExitDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Forward acceleration in m/s².</summary>
    [DataField]
    public float Thrust = 6f;

    /// <summary>Backward acceleration in m/s².</summary>
    [DataField]
    public float ReverseThrust = 4f;

    /// <summary>Sideways acceleration in m/s².</summary>
    [DataField]
    public float StrafeThrust = 5f;

    /// <summary>Top speed in open space, in m/s.</summary>
    [DataField]
    public float MaxSpeed = 80f;

    /// <summary>Top speed under gravity, in m/s.</summary>
    [DataField]
    public float TaxiSpeed = 2.5f;

    /// <summary>Deceleration while braking, parked or with nobody aboard, in m/s².</summary>
    [DataField]
    public float BrakeDeceleration = 20f;

    /// <summary>How fast drive mode bleeds off motion along an axis nobody is thrusting on, in m/s².</summary>
    [DataField]
    public float DriveDeceleration = 10f;

    [DataField, AutoNetworkedField]
    public FighterFlightMode FlightMode = FighterFlightMode.Drive;

    /// <summary>Angular acceleration in rad/s².</summary>
    [DataField]
    public float TurnAcceleration = 8f;

    /// <summary>Top turn rate in rad/s.</summary>
    [DataField]
    public float MaxTurnRate = 3f;

    /// <summary>Half-width of the cone around the nose the gun fires into, in degrees; aim outside it fires along its edge.</summary>
    [DataField]
    public float FireArc = 8f;

    /// <summary>Gunshot played at most once per <see cref="GunSoundInterval"/>; the gun's own sound when unset.</summary>
    [DataField]
    public SoundSpecifier? GunSound;

    [DataField]
    public TimeSpan GunSoundInterval = TimeSpan.FromSeconds(0.3);

    [ViewVariables]
    public TimeSpan NextGunSound;

    /// <summary>Damage the hull takes before it is destroyed, for the cockpit readout.</summary>
    [DataField]
    public float HullIntegrity = 3000f;

    /// <summary>Set when a burst empties the gun's cell; it stays locked until the cell is full again.</summary>
    [DataField, AutoNetworkedField]
    public bool GunRecharging;

    /// <summary>Whether the gun may fire while the fighter is on a grid.</summary>
    [DataField]
    public bool CanFireOnGrid;

    [DataField]
    public float Zoom = 3f;

    /// <summary>Whether the fighter turns to face the pilot's cursor instead of using the rotate keys.</summary>
    [DataField, AutoNetworkedField]
    public bool FollowCursor;

    /// <summary>World rotation the cursor last asked for, sent by the pilot's client.</summary>
    [ViewVariables]
    public Angle? CursorAngle;

    [DataField]
    public EntProtoId ExitAction = "WFActionFighterExit";

    [DataField]
    public EntProtoId FollowCursorAction = "WFActionFighterFollowCursor";

    [DataField]
    public EntProtoId CockpitAction = "WFActionFighterCockpit";

    [DataField]
    public EntityUid? ExitActionEntity;

    [DataField]
    public EntityUid? FollowCursorActionEntity;

    [DataField]
    public EntityUid? CockpitActionEntity;

    /// <summary>Breathable cockpit air, topped back up every time it is read.</summary>
    [ViewVariables]
    public GasMixture Air = new(70f);

    /// <summary>When the on-grid refusal popup may show again.</summary>
    [ViewVariables]
    public TimeSpan NextGridPopup;
}
