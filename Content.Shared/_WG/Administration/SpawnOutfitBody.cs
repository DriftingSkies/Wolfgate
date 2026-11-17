namespace Content.Shared._WG.Administration;

/// <summary>
/// Which body the Spawn as Outfit tool creates before dressing it.
/// </summary>
public enum SpawnOutfitBody : byte
{
    /// <summary>Random human appearance and name.</summary>
    Random,

    /// <summary>The stock default profile: a bald male human named John Doe.</summary>
    Default,

    /// <summary>The admin's currently selected character.</summary>
    Own,
}
