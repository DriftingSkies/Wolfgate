using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A hole that spawns mobs in a fixed cycle while fewer than <see cref="MaxAlive"/> of them live. A shovel closes it.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaBurrowComponent : Component
{
    /// <summary>
    /// Spawned in order, then from the start again.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Prototypes = new();

    /// <summary>
    /// Time between spawns while fewer than <see cref="MaxAlive"/> live.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(80);

    /// <summary>
    /// Spawns stop while this many of the hole's mobs are alive.
    /// </summary>
    [DataField]
    public int MaxAlive = 15;

    /// <summary>
    /// Time to fill the hole with a shovel, before the shovel's speed modifier.
    /// </summary>
    [DataField]
    public TimeSpan CloseDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether a shovel can fill it in. Off for a mob that spawns others off its own body.
    /// </summary>
    [DataField]
    public bool Closable = true;

    /// <summary>
    /// Played at the hole on each spawn.
    /// </summary>
    [DataField]
    public SoundSpecifier? SpawnSound;

    /// <summary>
    /// Throws each spawn at the mob's target at this speed. Zero drops it in place.
    /// </summary>
    [DataField]
    public float ThrowSpeed;

    /// <summary>
    /// When the next spawn happens.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextSpawn;

    /// <summary>
    /// Position in <see cref="Prototypes"/> of the next spawn.
    /// </summary>
    [DataField]
    public int Index;

    /// <summary>
    /// Mobs spawned by the hole, counted against <see cref="MaxAlive"/>.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Spawned = new();
}
