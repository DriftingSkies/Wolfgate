using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Fires bursts of projectiles: a spread or a spiral when its action is used, and a ring when the mob dies.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaProjectileBurstComponent : Component
{
    /// <summary>
    /// Projectile fired by every burst unless the action names its own.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Projectile;

    /// <summary>
    /// Projectile speed, in tiles per second.
    /// </summary>
    [DataField]
    public float Speed = 15f;

    /// <summary>
    /// Shots fanned around the target's direction.
    /// </summary>
    [DataField]
    public int SpreadCount = 3;

    /// <summary>
    /// Angle between neighbouring shots of the spread.
    /// </summary>
    [DataField]
    public Angle SpreadStep = Angle.FromDegrees(10);

    /// <summary>
    /// Chance the action fires a spiral instead of the spread.
    /// </summary>
    [DataField]
    public float SpiralChance;

    /// <summary>
    /// Shots fired in a spiral.
    /// </summary>
    [DataField]
    public int SpiralCount = 10;

    /// <summary>
    /// Angle each spiral shot turns from the one before it.
    /// </summary>
    [DataField]
    public Angle SpiralStep = Angle.FromDegrees(180 / Math.E);

    /// <summary>
    /// Time between spiral shots.
    /// </summary>
    [DataField]
    public TimeSpan SpiralDelay = TimeSpan.FromSeconds(0.3);

    /// <summary>
    /// When above zero, the action fires this many shots one after another at the target instead of the spread,
    /// each off by up to <see cref="VolleyJitter"/>.
    /// </summary>
    [DataField]
    public int VolleyCount;

    /// <summary>
    /// Largest random angle a volley shot strays from the target.
    /// </summary>
    [DataField]
    public Angle VolleyJitter = Angle.FromDegrees(10);

    /// <summary>
    /// Time between volley shots.
    /// </summary>
    [DataField]
    public TimeSpan VolleyDelay = TimeSpan.FromSeconds(0.1);

    /// <summary>
    /// A hit taken during the wind-up or a volley cancels the rest of it.
    /// </summary>
    [DataField]
    public bool CancelOnHit;

    /// <summary>
    /// Delay between using the action and the first shot.
    /// </summary>
    [DataField]
    public TimeSpan Windup = TimeSpan.Zero;

    /// <summary>
    /// Played when the wind-up starts.
    /// </summary>
    [DataField]
    public SoundSpecifier? WindupSound;

    /// <summary>
    /// Played when a spread or volley fires.
    /// </summary>
    [DataField]
    public SoundSpecifier? SpreadSound;

    /// <summary>
    /// Played when a spiral starts.
    /// </summary>
    [DataField]
    public SoundSpecifier? SpiralSound;

    /// <summary>
    /// Shown at the mob when the wind-up starts, unless the action names its own.
    /// </summary>
    [DataField]
    public LocId? WindupPopup;

    /// <summary>
    /// Turns the mob's PointLight on during the wind-up and off after it.
    /// </summary>
    [DataField]
    public bool WindupLight;

    /// <summary>
    /// Shakes the mob this hard during the wind-up. 0 for no shake.
    /// </summary>
    [DataField]
    public float WindupJitter;

    /// <summary>
    /// Shown at the mob when a spread or volley fires.
    /// </summary>
    [DataField]
    public LocId? SpreadPopup;

    /// <summary>
    /// Shown at the mob when a spiral starts.
    /// </summary>
    [DataField]
    public LocId? SpiralPopup;

    /// <summary>
    /// Shots fired in an even ring when the mob dies or is destroyed. 0 for none.
    /// </summary>
    [DataField]
    public int DeathCount;

    /// <summary>
    /// Entity the current burst aims at, set by the action.
    /// </summary>
    [DataField]
    public EntityUid? Target;

    /// <summary>
    /// When the wind-up ends and the burst fires. Null when no wind-up is running.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan? FireAt;

    /// <summary>
    /// Spiral shots still to fire.
    /// </summary>
    [DataField]
    public int SpiralShotsLeft;

    /// <summary>
    /// When the next spiral shot fires.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextSpiralShot;

    /// <summary>
    /// Volley shots still to fire.
    /// </summary>
    [DataField]
    public int VolleyShotsLeft;

    /// <summary>
    /// When the next volley shot fires.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextVolleyShot;

    /// <summary>
    /// Set once the death ring has fired, so it fires only once.
    /// </summary>
    [DataField]
    public bool DeathBurstDone;

    /// <summary>
    /// Projectiles still in flight, so their shooter can be cleared when this mob is deleted.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Shots = new();

    /// <summary>
    /// Projectile chosen by the action that started the current burst, if it overrides <see cref="Projectile"/>.
    /// </summary>
    [ViewVariables]
    public EntProtoId? ActionProjectile;
}
