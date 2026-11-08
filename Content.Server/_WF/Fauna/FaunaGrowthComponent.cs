using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Replaces the mob with its adult form after a random time, while it is alive.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaGrowthComponent : Component
{
    /// <summary>
    /// Adult form the mob is replaced with.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId GrowInto;

    /// <summary>
    /// When set, the mob grows into a random one of these instead of <see cref="GrowInto"/>.
    /// </summary>
    [DataField]
    public List<EntProtoId> GrowIntoAny = new();

    /// <summary>
    /// Chance the mob grows up at all. Otherwise it stays young.
    /// </summary>
    [DataField]
    public float Chance = 1f;

    /// <summary>
    /// Shortest time from spawn to growing up.
    /// </summary>
    [DataField]
    public TimeSpan MinDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Longest time from spawn to growing up.
    /// </summary>
    [DataField]
    public TimeSpan MaxDelay = TimeSpan.FromMinutes(20);

    /// <summary>
    /// When the mob grows up.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan GrowAt;
}
