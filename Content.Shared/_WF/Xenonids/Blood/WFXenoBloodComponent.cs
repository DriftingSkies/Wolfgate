using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Blood;

/// <summary>A xeno splatters acid blood when hurt and when it dies, and its corpse gibs under enough damage.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFXenoBloodComponent : Component
{
    /// <summary>Spawner that drops one blood decal where the xeno stands.</summary>
    [DataField]
    public EntProtoId Splatter = "WFDecalSpawnerXenoBlood";

    /// <summary>Brute damage one hit must deal before it splatters.</summary>
    [DataField]
    public FixedPoint2 DamagePerSplatter = 12;

    /// <summary>Minimum time between hit splatters.</summary>
    [DataField]
    public TimeSpan SplatterCooldown = TimeSpan.FromSeconds(0.3);

    [DataField]
    public int DeathSplatters = 3;

    [DataField]
    public int GibSplatters = 4;

    /// <summary>A dead xeno gibs once its total damage reaches this many times its death threshold.</summary>
    [DataField]
    public float GibDamageMultiplier = 2f;

    [DataField]
    public TimeSpan NextSplatter;
}
