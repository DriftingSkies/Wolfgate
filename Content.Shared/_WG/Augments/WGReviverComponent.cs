using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.Augments;

/// <summary>
/// A reviver in the torso: when the heart stops it holds the body's bleeding, runs a reserve of blood in and shocks,
/// on the same rules and the same odds as a medic's paddles, and then has to recharge. The hold outlasts the shock.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WGReviverComponent : Component
{
    /// <summary>How long it charges before the first shock.</summary>
    [DataField]
    public TimeSpan ChargeTime = TimeSpan.FromSeconds(10);

    /// <summary>The wait after a shock that didn't restart the heart.</summary>
    [DataField]
    public TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    /// <summary>How many shocks it has in it before it must recharge.</summary>
    [DataField]
    public int Shocks = 3;

    /// <summary>The recharge after the heart restarts or the last shock fails.</summary>
    [DataField]
    public TimeSpan Recharge = TimeSpan.FromMinutes(10);

    /// <summary>How long an EMP keeps it from firing.</summary>
    [DataField]
    public TimeSpan EmpLockout = TimeSpan.FromMinutes(2);

    /// <summary>How long the bleeding stays held after the shock it is charging: its owner's time to dress it.</summary>
    [DataField]
    public TimeSpan HoldTime = TimeSpan.FromMinutes(3);

    /// <summary>The blood it carries, which it has back when it has recharged.</summary>
    [DataField]
    public float Reserve = 60f;

    /// <summary>The blood it runs in each second it is charging, so the whole reserve is in by the first shock.</summary>
    [DataField]
    public float InfuseRate = 6f;

    [DataField]
    public SoundSpecifier? ChargeSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_charge.ogg");

    [DataField]
    public SoundSpecifier? ZapSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_zap.ogg");

    [DataField]
    public SoundSpecifier? SuccessSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_success.ogg");

    [DataField]
    public SoundSpecifier? FailureSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_failed.ogg");

    /// <summary>When the next shock lands, while it is charging one.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? NextShock;

    /// <summary>The shocks given since the heart stopped.</summary>
    [DataField]
    public int ShocksGiven;

    /// <summary>The blood run in since it last recharged.</summary>
    [DataField]
    public float Infused;

    /// <summary>When it can fire again.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan ReadyAt;
}
