using Content.Shared.Storage;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Spawns entities around the mob when it dies, such as a brood bursting out of its body. Can also gib the mob, make it
/// vanish, or throw out what it carries.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaDeathSpawnComponent : Component
{
    /// <summary>
    /// Entities spawned around the mob when it dies.
    /// </summary>
    [DataField]
    public List<EntitySpawnEntry> Spawns = new();

    /// <summary>
    /// Random offset in tiles for each spawned entity.
    /// </summary>
    [DataField]
    public float Scatter = 0.5f;

    /// <summary>
    /// Popup shown on the mob when it dies.
    /// </summary>
    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Gibs the body on death, or deletes it if it has none.
    /// </summary>
    [DataField]
    public bool Gib;

    /// <summary>
    /// Chance the mob is deleted on death with no spawns, popup or gibs.
    /// </summary>
    [DataField]
    public float VanishChance;

    /// <summary>
    /// Throws out everything the mob holds or has stuck in it this many tiles. Zero turns it off.
    /// </summary>
    [DataField]
    public float ThrowContents;
}
